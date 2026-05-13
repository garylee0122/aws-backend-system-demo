using StackExchange.Redis;
using System.Text.Json;

namespace DemoAPI.Infrastructure.Services
{
    public class RedisQueueService
    {
        private readonly IDatabase _db;
        private const string QueueKey = "order_queue";
        private const string ProcessingQueueKey = "order_queue:processing";

        public RedisQueueService(IConnectionMultiplexer redis)
        {
            _db = redis.GetDatabase();
        }

        public async Task EnqueueAsync(object data)
        {
            var json = JsonSerializer.Serialize(data);
            await _db.ListLeftPushAsync(QueueKey, json);
        }

        public async Task<string?> DequeueForProcessingAsync()
        {
            var result = await _db.ListRightPopLeftPushAsync(QueueKey, ProcessingQueueKey);
            return result;
        }

        public async Task CompleteProcessingAsync(string data)
        {
            await _db.ListRemoveAsync(ProcessingQueueKey, data, 1);
        }

        public async Task RequeueProcessingAsync(string data)
        {
            await _db.ListRemoveAsync(ProcessingQueueKey, data, 1);
            await _db.ListLeftPushAsync(QueueKey, data);
        }

        public async Task<long> GetLengthAsync()
        {
            return await _db.ListLengthAsync(QueueKey);
        }
    }
}
