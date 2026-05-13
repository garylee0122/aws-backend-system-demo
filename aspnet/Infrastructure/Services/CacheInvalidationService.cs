using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DemoAPI.Infrastructure.Services
{
    public class CacheInvalidationService
    {
        private const string ProductCacheIndexKey = "products_cache_keys";
        private const string OrderCacheIndexKey = "orders_cache_keys";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IDistributedCache _redisCache;
        private readonly ILogger<CacheInvalidationService> _logger;

        public CacheInvalidationService(IDistributedCache redisCache, ILogger<CacheInvalidationService> logger)
        {
            _redisCache = redisCache;
            _logger = logger;
        }

        public async Task TrackOrderCacheKeyAsync(string cacheKey)
        {
            var cacheKeys = await GetTrackedCacheKeysAsync(OrderCacheIndexKey);
            cacheKeys.Add(cacheKey);

            await _redisCache.SetStringAsync(
                OrderCacheIndexKey,
                JsonSerializer.Serialize(cacheKeys, JsonOptions));
        }

        public async Task InvalidateAllProductCacheAsync()
        {
            var cacheKeys = await GetTrackedCacheKeysAsync(ProductCacheIndexKey);
            if (cacheKeys.Count == 0)
            {
                _logger.LogInformation("Product redis cache invalidation skipped because no cache keys were tracked.");
                return;
            }

            foreach (var key in cacheKeys)
            {
                await _redisCache.RemoveAsync(key);
                _logger.LogInformation("Product redis cache invalidated. CacheKey: {CacheKey}", key);
            }

            await _redisCache.RemoveAsync(ProductCacheIndexKey);
        }

        public async Task InvalidateUserOrderCacheAsync(int userId)
        {
            var cacheKeys = await GetTrackedCacheKeysAsync(OrderCacheIndexKey);
            if (cacheKeys.Count == 0)
            {
                _logger.LogInformation(
                    "Order redis cache invalidation skipped because no cache keys were tracked. UserId: {UserId}",
                    userId);
                return;
            }

            var userOrderListToken = $"user:{userId}:orders:";
            var userOrderItemToken = $"user:{userId}:order:";
            var keysToRemove = cacheKeys
                .Where(key => key.Contains(userOrderListToken) || key.Contains(userOrderItemToken))
                .ToList();

            foreach (var key in keysToRemove)
            {
                await _redisCache.RemoveAsync(key);
                cacheKeys.Remove(key);
                _logger.LogInformation(
                    "Order redis cache invalidated. CacheKey: {CacheKey}, UserId: {UserId}",
                    key,
                    userId);
            }

            await _redisCache.SetStringAsync(
                OrderCacheIndexKey,
                JsonSerializer.Serialize(cacheKeys, JsonOptions));
        }

        private async Task<HashSet<string>> GetTrackedCacheKeysAsync(string indexKey)
        {
            var cacheKeysJson = await _redisCache.GetStringAsync(indexKey);
            if (string.IsNullOrEmpty(cacheKeysJson))
            {
                return new HashSet<string>();
            }

            return JsonSerializer.Deserialize<HashSet<string>>(cacheKeysJson, JsonOptions) ?? new HashSet<string>();
        }
    }
}
