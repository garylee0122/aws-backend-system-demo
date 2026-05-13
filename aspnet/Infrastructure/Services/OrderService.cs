using DemoAPI.Data;
using DemoAPI.DTOs;
using DemoAPI.Enums;
using DemoAPI.Infrastructure.Queues;
using DemoAPI.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DemoAPI.Infrastructure.Services
{
    public class OrderService
    {
        private const int DefaultPage = 1;
        private const int PageSize = 5;
        private const int OrderCacheTtlSeconds = 300;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly AppDbContext _context;
        private readonly OrderQueue _orderQueue;
        private readonly ILogger<OrderService> _logger;
        private readonly RedisQueueService _redisQueue;
        private readonly IDistributedCache _redisCache;
        private readonly CacheInvalidationService _cacheInvalidationService;

        public OrderService(
            AppDbContext context,
            OrderQueue orderQueue,
            RedisQueueService redisQueue,
            IDistributedCache redisCache,
            CacheInvalidationService cacheInvalidationService,
            ILogger<OrderService> logger)
        {
            _context = context;
            _orderQueue = orderQueue;
            _redisQueue = redisQueue;
            _redisCache = redisCache;
            _cacheInvalidationService = cacheInvalidationService;
            _logger = logger;
        }

        public async Task<CreateOrderResult> Create(CreateOrderDto dto, int userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (dto.Items == null || dto.Items.Count == 0)
                {
                    return CreateOrderResult.InvalidOrder("Order must contain at least one item");
                }

                var invalidItem = dto.Items.FirstOrDefault(item => item.Quantity <= 0);
                if (invalidItem != null)
                {
                    return CreateOrderResult.InvalidOrder($"Product {invalidItem.ProductId} has invalid quantity");
                }

                var productIds = dto.Items
                    .Select(item => item.ProductId)
                    .Distinct()
                    .ToList();

                var products = await _context.Products
                    .Where(product => productIds.Contains(product.Id))
                    .ToDictionaryAsync(product => product.Id);

                var missingProductId = productIds
                    .Cast<int?>()
                    .FirstOrDefault(productId => !products.ContainsKey(productId!.Value));

                if (missingProductId.HasValue)
                {
                    return CreateOrderResult.ProductNotFound(missingProductId.Value);
                }

                var requestedQuantities = dto.Items
                    .GroupBy(item => item.ProductId)
                    .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));

                var insufficientStockProduct = requestedQuantities
                    .Select(entry => new
                    {
                        ProductId = entry.Key,
                        RequestedQuantity = entry.Value,
                        Product = products[entry.Key]
                    })
                    .FirstOrDefault(entry => entry.Product.Stock < entry.RequestedQuantity);

                if (insufficientStockProduct != null)
                {
                    return CreateOrderResult.InsufficientStock(
                        insufficientStockProduct.ProductId,
                        insufficientStockProduct.Product.Stock,
                        insufficientStockProduct.RequestedQuantity);
                }

                foreach (var requestedQuantity in requestedQuantities)
                {
                    products[requestedQuantity.Key].Stock -= requestedQuantity.Value;
                }

                var orderItems = dto.Items
                    .Select(item =>
                    {
                        var product = products[item.ProductId];

                        return new OrderItem
                        {
                            ProductId = product.Id,
                            Price = product.Price,
                            Quantity = item.Quantity
                        };
                    })
                    .ToList();

                var order = new Order
                {
                    UserId = userId,
                    TotalPrice = orderItems.Sum(item => item.Price * item.Quantity),
                    Status = OrderStatus.Pending,
                    Items = orderItems
                };

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Order {OrderId} created.", order.Id);

                // 商品庫存變動後，清除商品相關快取。
                await _cacheInvalidationService.InvalidateAllProductCacheAsync();

                // 訂單資料有新增時，清除該使用者的訂單列表與單筆訂單快取。
                await _cacheInvalidationService.InvalidateUserOrderCacheAsync(userId);

                // _orderQueue.Enqueue(new OrderQueueItem { OrderId = order.Id }); // 保留原本的 memory queue 寫法
                await _redisQueue.EnqueueAsync(new OrderQueueItem { OrderId = order.Id });
                _logger.LogInformation("Order {OrderId} enqueued to Redis queue for processing.", order.Id);

                await transaction.CommitAsync();

                return CreateOrderResult.Success(MapToOrderDto(order));
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<List<OrderDto>> GetMyOrders(int userId, int page = DefaultPage)
        {
            page = page < 1 ? DefaultPage : page;
            var cacheKey = GetUserOrdersCacheKey(userId, page);
            var cachedJson = await _redisCache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedJson))
            {
                _logger.LogInformation("Orders redis cache hit. CacheKey: {CacheKey}, UserId: {UserId}", cacheKey, userId);
                return JsonSerializer.Deserialize<List<OrderDto>>(cachedJson, JsonOptions) ?? new List<OrderDto>();
            }

            /*
            var orders = await _context.Orders
                .Where(order => order.UserId == userId)
                .Include(order => order.Items)
                .ThenInclude(item => item.Product)
                .OrderByDescending(order => order.Id)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            return orders.Select(MapToOrderDto).ToList();
            */

            var orders = await _context.Orders
                .Where(order => order.UserId == userId)
                .Include(order => order.Items)
                .ThenInclude(item => item.Product)
                .OrderByDescending(order => order.Id)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            var orderDtos = orders.Select(MapToOrderDto).ToList();
            await SetOrderCacheAsync(cacheKey, orderDtos);

            return orderDtos;
        }

        public async Task<OrderDto?> GetById(int id, int userId)
        {
            var cacheKey = GetUserOrderCacheKey(userId, id);
            var cachedJson = await _redisCache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedJson))
            {
                _logger.LogInformation(
                    "Order redis cache hit. CacheKey: {CacheKey}, UserId: {UserId}, OrderId: {OrderId}",
                    cacheKey,
                    userId,
                    id);

                return JsonSerializer.Deserialize<OrderDto>(cachedJson, JsonOptions);
            }

            /*
            var order = await _context.Orders
                .Where(order => order.Id == id && order.UserId == userId)
                .Include(order => order.Items)
                .ThenInclude(item => item.Product)
                .FirstOrDefaultAsync();

            return order == null ? null : MapToOrderDto(order);
            */

            var order = await _context.Orders
                .Where(order => order.Id == id && order.UserId == userId)
                .Include(order => order.Items)
                .ThenInclude(item => item.Product)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                return null;
            }

            var orderDto = MapToOrderDto(order);
            await SetOrderCacheAsync(cacheKey, orderDto);

            return orderDto;
        }

        private async Task SetOrderCacheAsync<T>(string cacheKey, T value)
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(OrderCacheTtlSeconds)
            };

            await _redisCache.SetStringAsync(cacheKey, json, cacheOptions);
            await _cacheInvalidationService.TrackOrderCacheKeyAsync(cacheKey);
        }

        private static string GetUserOrdersCacheKey(int userId, int page)
        {
            return $"user:{userId}:orders:page:{page}";
        }

        private static string GetUserOrderCacheKey(int userId, int orderId)
        {
            return $"user:{userId}:order:{orderId}";
        }

        private static OrderDto MapToOrderDto(Order order)
        {
            return new OrderDto
            {
                Id = order.Id,
                TotalPrice = order.TotalPrice,
                Status = order.Status.ToString(),
                Items = order.Items.Select(item => new OrderItemDto
                {
                    ProductId = item.ProductId,
                    ProductName = item.Product?.Name ?? string.Empty,
                    Price = item.Price,
                    Quantity = item.Quantity
                }).ToList()
            };
        }
    }

    public class CreateOrderResult
    {
        public bool IsSuccess { get; init; }
        public int StatusCode { get; init; }
        public string? ErrorMessage { get; init; }
        public OrderDto? Data { get; init; }

        public static CreateOrderResult Success(OrderDto data) => new()
        {
            IsSuccess = true,
            StatusCode = StatusCodes.Status200OK,
            Data = data
        };

        public static CreateOrderResult ProductNotFound(int productId) => new()
        {
            IsSuccess = false,
            StatusCode = StatusCodes.Status404NotFound,
            ErrorMessage = $"Product {productId} not found"
        };

        public static CreateOrderResult InsufficientStock(int productId, int availableStock, int requestedQuantity) => new()
        {
            IsSuccess = false,
            StatusCode = StatusCodes.Status409Conflict,
            ErrorMessage = $"Product {productId} stock is insufficient. Available: {availableStock}, Requested: {requestedQuantity}"
        };

        public static CreateOrderResult InvalidOrder(string errorMessage) => new()
        {
            IsSuccess = false,
            StatusCode = StatusCodes.Status400BadRequest,
            ErrorMessage = errorMessage
        };
    }
}
