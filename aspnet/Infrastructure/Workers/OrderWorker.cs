using DemoAPI.Data;
using DemoAPI.DTOs;
using DemoAPI.Enums;
using DemoAPI.Infrastructure.Queues;
using DemoAPI.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace DemoAPI.Infrastructure.Workers
{
    public class OrderWorker : BackgroundService
    {
        private readonly OrderQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderWorker> _logger;
        private readonly RedisQueueService _redisQueue;
        private readonly IServiceProvider _serviceProvider;
        private readonly RedisLockService _lockService;
        private readonly CacheInvalidationService _cacheInvalidationService;
        private static readonly JsonSerializerOptions QueueJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public OrderWorker(
            OrderQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<OrderWorker> logger,
            RedisQueueService redisQueue,
            IServiceProvider serviceProvider,
            RedisLockService lockService,
            CacheInvalidationService cacheInvalidationService)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _redisQueue = redisQueue;
            _serviceProvider = serviceProvider;
            _lockService = lockService;
            _cacheInvalidationService = cacheInvalidationService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("OrderWorker started...");
            _logger.LogInformation("OrderWorker started...");

            try
            {
                var initialLength = await _redisQueue.GetLengthAsync();
                _logger.LogInformation("Redis order_queue length on startup: {QueueLength}", initialLength);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read Redis order_queue length on startup.");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var data = await _redisQueue.DequeueForProcessingAsync();
                    if (data == null)
                    {
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    OrderQueueItem? queueItem;
                    try
                    {
                        queueItem = DeserializeQueueItem(data!);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to deserialize Redis order_queue payload: {Payload}", data.ToString());
                        await _redisQueue.CompleteProcessingAsync(data);
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    if (queueItem == null)
                    {
                        _logger.LogWarning("Received null queue item from Redis payload: {Payload}", data.ToString());
                        await _redisQueue.CompleteProcessingAsync(data);
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    int orderId = queueItem.OrderId;
                    var lockKey = $"lock:order:{orderId}";
                    var lockToken = await _lockService.AcquireLockAsync(lockKey);
                    if (lockToken == null)
                    {
                        Console.WriteLine("Order {0} is already being processed, returning job to queue...", orderId);
                        _logger.LogWarning("Order {OrderId} is already being processed, returning job to queue...", orderId);
                        await _redisQueue.RequeueProcessingAsync(data);
                        await Task.Delay(500, stoppingToken);
                        continue;
                    }

                    try
                    {
                        Console.WriteLine($"Processing order {queueItem.OrderId} from Redis queue");
                        _logger.LogInformation("Processing order {OrderId} from Redis queue", queueItem.OrderId);

                        //if (queueItem.RetryCount < 2)
                        //{
                        //    throw new Exception("Random failure");
                        //}

                        await Task.Delay(3000, stoppingToken);
                        await UpdateOrderStatusAsync(queueItem.OrderId, OrderStatus.Created, stoppingToken);
                        await _redisQueue.CompleteProcessingAsync(data);

                        Console.WriteLine("Processing order {0} Success from Redis queue!", queueItem.OrderId);
                        _logger.LogInformation("Processing order {OrderId} Success from Redis queue!", queueItem.OrderId);
                    }
                    catch (Exception ex)
                    {
                        queueItem.RetryCount++;

                        if (queueItem.RetryCount < 3)
                        {
                            Console.WriteLine($"Retry {queueItem.RetryCount} for order {queueItem.OrderId} from Redis queue");
                            _logger.LogError("Retry {RetryCount} for order {OrderId} from Redis queue", queueItem.RetryCount, queueItem.OrderId);

                            var delay = (int)Math.Pow(2, queueItem.RetryCount) * 1000;
                            await _redisQueue.CompleteProcessingAsync(data);
                            await Task.Delay(delay, stoppingToken);
                            await _redisQueue.EnqueueAsync(queueItem);
                        }
                        else
                        {
                            await UpdateOrderStatusAsync(queueItem.OrderId, OrderStatus.Failed, stoppingToken);
                            await _redisQueue.CompleteProcessingAsync(data);

                            Console.WriteLine($"Order {queueItem.OrderId} failed permanently");
                            _logger.LogError("Order {OrderId} failed permanently", queueItem.OrderId);
                        }

                        Console.WriteLine($"Failed to process order {queueItem.OrderId} from Redis queue: {ex.Message}");
                        _logger.LogError(ex, "Failed to process order {OrderId} from Redis queue", queueItem.OrderId);
                    }
                    finally
                    {
                        var released = await _lockService.ReleaseLockAsync(lockKey, lockToken);
                        if (!released)
                        {
                            _logger.LogWarning("Lock for order {OrderId} was not released because ownership changed or it already expired.", orderId);
                        }

                        Console.WriteLine("Lock for order {0} was released", orderId);
                        _logger.LogInformation("Lock for order {OrderId} was released", orderId);
                    }
                }
                catch (RedisConnectionException ex)
                {
                    _logger.LogError(ex, "Redis connection error in OrderWorker.");
                    await Task.Delay(3000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in OrderWorker loop.");
                    await Task.Delay(3000, stoppingToken);
                }
            }

            Console.WriteLine("OrderWorker stopped.");
            _logger.LogInformation("OrderWorker stopped.");
        }

        private async Task UpdateOrderStatusAsync(int orderId, OrderStatus status, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var order = await context.Orders.FindAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Order {OrderId} not found when updating status to {Status}", orderId, status);
                return;
            }

            order.Status = status;
            await context.SaveChangesAsync(cancellationToken);
            await _cacheInvalidationService.InvalidateUserOrderCacheAsync(order.UserId);
        }

        private static OrderQueueItem? DeserializeQueueItem(string data)
        {
            var queueItem = JsonSerializer.Deserialize<OrderQueueItem>(data, QueueJsonOptions);
            if (queueItem?.OrderId > 0)
            {
                return queueItem;
            }

            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;

            if (!TryReadInt(root, "OrderId", out var orderId) &&
                !TryReadInt(root, "orderId", out orderId) &&
                !TryReadInt(root, "order_id", out orderId))
            {
                return queueItem;
            }

            TryReadInt(root, "RetryCount", out var retryCount);
            if (retryCount == 0)
            {
                TryReadInt(root, "retryCount", out retryCount);
            }
            if (retryCount == 0)
            {
                TryReadInt(root, "retry_count", out retryCount);
            }

            return new OrderQueueItem
            {
                OrderId = orderId,
                RetryCount = retryCount
            };
        }

        private static bool TryReadInt(JsonElement root, string propertyName, out int value)
        {
            value = 0;
            if (!root.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
            {
                return true;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
            {
                return true;
            }

            return false;
        }
    }
}
