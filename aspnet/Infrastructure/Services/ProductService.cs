using DemoAPI.Data;
using DemoAPI.DTOs;
using DemoAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace DemoAPI.Infrastructure.Services
{
    public class ProductService
    {
        private const int PageSize = 5;
        private const string ProductCacheIndexKey = "products_cache_keys";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly IDistributedCache _redisCache;
        private readonly ILogger<ProductService> _logger;

        public ProductService(AppDbContext context, IMemoryCache cache, IDistributedCache redisCache, ILogger<ProductService> logger)
        {
            _context = context;
            _cache = cache;
            _redisCache = redisCache;
            _logger = logger;
        }

        public async Task<PagedResultDto<ProductResponseDto>> GetAll(string? keyword, int page, int? userId)
        {
            page = page < 1 ? 1 : page;

            var products = await GetProducts(keyword ?? "", userId);

            var totalCount = products.Count;
            var items = products
                .OrderBy(p => p.Id)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(p => new ProductResponseDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price,
                    Stock = p.Stock,
                    UserId = p.UserId
                })
                .ToList();

            return new PagedResultDto<ProductResponseDto>
            {
                Items = items,
                Page = page,
                PageSize = PageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize)
            };
        }

        public async Task<List<Product>> GetProducts(string keyword, int? userId)
        {
            var cacheKey = $"products_{keyword ?? "all"}_user_{userId?.ToString() ?? "anonymous"}";
            var stopwatch = Stopwatch.StartNew();

            /*
             * 以下是原本使用 Memory Cache 的寫法，完整保留作為對照。
             *
             * if (!_cache.TryGetValue(cacheKey, out List<Product>? products))
             * {
             *     _logger.LogInformation("Products cache miss. Loading from DB. CacheKey: {CacheKey}, Keyword: {Keyword}, UserId: {UserId}", cacheKey, keyword, userId);
             *
             *     var query = _context.Products.AsQueryable();
             *
             *     if (!string.IsNullOrEmpty(keyword))
             *     {
             *         query = query.Where(p => p.Name.Contains(keyword));
             *     }
             *
             *     if (userId != null)
             *     {
             *         query = query.Where(p => p.UserId == userId);
             *     }
             *
             *     products = await query.ToListAsync();
             *
             *     var cacheOptions = new MemoryCacheEntryOptions()
             *         .SetAbsoluteExpiration(TimeSpan.FromSeconds(60));
             *
             *     _cache.Set(cacheKey, products, cacheOptions);
             *     TrackProductCacheKey(cacheKey);
             * }
             * else
             * {
             *     _logger.LogInformation("Products cache hit. Loading from cache. CacheKey: {CacheKey}, Keyword: {Keyword}, UserId: {UserId}", cacheKey, keyword, userId);
             * }
             *
             * return products!;
             */

            var cachedJson = await _redisCache.GetStringAsync(cacheKey);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "Products redis cache hit. CacheKey: {CacheKey}, Keyword: {Keyword}, UserId: {UserId}, ElapsedMilliseconds: {ElapsedMilliseconds}",
                    cacheKey,
                    keyword,
                    userId,
                    stopwatch.ElapsedMilliseconds);

                return JsonSerializer.Deserialize<List<Product>>(cachedJson, JsonOptions) ?? new List<Product>();
            }

            _logger.LogInformation(
                "Products redis cache miss. Loading from DB. CacheKey: {CacheKey}, Keyword: {Keyword}, UserId: {UserId}",
                cacheKey,
                keyword,
                userId);

            var query = _context.Products.AsQueryable();

            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(p => p.Name.Contains(keyword));
            }

            if (userId != null)
            {
                query = query.Where(p => p.UserId == userId);
            }

            var products = await query.ToListAsync();
            stopwatch.Stop();

            _logger.LogInformation(
                "Products loaded from DB. CacheKey: {CacheKey}, Keyword: {Keyword}, UserId: {UserId}, Count: {Count}, ElapsedMilliseconds: {ElapsedMilliseconds}",
                cacheKey,
                keyword,
                userId,
                products.Count,
                stopwatch.ElapsedMilliseconds);

            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
            };

            var json = JsonSerializer.Serialize(products, JsonOptions);
            await _redisCache.SetStringAsync(cacheKey, json, cacheOptions);
            await TrackProductCacheKey(cacheKey);

            return products;
        }

        public async Task<ProductResponseDto?> GetById(int id, int? userId)
        {
            return await _context.Products
                .Where(p => p.Id == id && p.UserId == userId)
                .Select(p => new ProductResponseDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price,
                    Stock = p.Stock,
                    UserId = p.UserId
                })
                .FirstOrDefaultAsync();
        }

        public async Task<ProductResponseDto> Create(CreateProductDto dto)
        {
            var product = new Product
            {
                Name = dto.Name,
                Price = dto.Price,
                Stock = dto.Stock,
                UserId = dto.UserId ?? 0
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            await InvalidateProductCache(product.UserId);

            return new ProductResponseDto
            {
                Id = product.Id,
                Name = product.Name,
                Price = product.Price,
                Stock = product.Stock,
                UserId = product.UserId
            };
        }

        public async Task<ProductResponseDto?> Update(int id, UpdateProductDto product)
        {
            var existing = await _context.Products
                .Where(p => p.Id == id && p.UserId == product.UserId)
                .FirstOrDefaultAsync();

            if (existing == null)
            {
                return null;
            }

            existing.Name = product.Name;
            existing.Price = product.Price;
            existing.Stock = product.Stock;

            await _context.SaveChangesAsync();
            await InvalidateProductCache(existing.UserId);

            return new ProductResponseDto
            {
                Id = existing.Id,
                Name = existing.Name,
                Price = existing.Price,
                Stock = existing.Stock,
                UserId = existing.UserId
            };
        }

        public async Task<bool> Delete(int id, int? userId)
        {
            var product = await _context.Products
                .Where(p => p.Id == id && p.UserId == userId)
                .FirstOrDefaultAsync();

            if (product == null)
            {
                return false;
            }

            var cacheUserId = product.UserId;
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
            await InvalidateProductCache(cacheUserId);

            return true;
        }

        private async Task TrackProductCacheKey(string cacheKey)
        {
            /*
             * 以下是原本使用 Memory Cache 追蹤快取 key 的寫法，完整保留作為對照。
             *
             * var cacheKeys = _cache.Get<HashSet<string>>(ProductCacheIndexKey) ?? new HashSet<string>();
             * cacheKeys.Add(cacheKey);
             * _cache.Set(ProductCacheIndexKey, cacheKeys);
             */

            var cacheKeys = await GetTrackedCacheKeys();
            cacheKeys.Add(cacheKey);

            await _redisCache.SetStringAsync(
                ProductCacheIndexKey,
                JsonSerializer.Serialize(cacheKeys, JsonOptions));
        }

        private async Task InvalidateProductCache(int? userId)
        {
            /*
             * 以下是原本使用 Memory Cache 清除快取的寫法，完整保留作為對照。
             *
             * var cacheKeys = _cache.Get<HashSet<string>>(ProductCacheIndexKey);
             * if (cacheKeys == null || cacheKeys.Count == 0)
             * {
             *     _logger.LogInformation("Product cache invalidation skipped because no cache keys were tracked. UserId: {UserId}", userId);
             *     return;
             * }
             *
             * var userToken = $"_user_{userId?.ToString() ?? "anonymous"}";
             * var keysToRemove = cacheKeys
             *     .Where(key => key.Contains(userToken))
             *     .ToList();
             *
             * foreach (var key in keysToRemove)
             * {
             *     _cache.Remove(key);
             *     cacheKeys.Remove(key);
             *     _logger.LogInformation("Product cache invalidated. CacheKey: {CacheKey}, UserId: {UserId}", key, userId);
             * }
             *
             * _cache.Set(ProductCacheIndexKey, cacheKeys);
             */

            var cacheKeys = await GetTrackedCacheKeys();
            if (cacheKeys.Count == 0)
            {
                _logger.LogInformation(
                    "Product redis cache invalidation skipped because no cache keys were tracked. UserId: {UserId}",
                    userId);
                return;
            }

            var userToken = $"_user_{userId?.ToString() ?? "anonymous"}";
            var keysToRemove = cacheKeys
                .Where(key => key.Contains(userToken))
                .ToList();

            foreach (var key in keysToRemove)
            {
                await _redisCache.RemoveAsync(key);
                cacheKeys.Remove(key);
                _logger.LogInformation(
                    "Product redis cache invalidated. CacheKey: {CacheKey}, UserId: {UserId}",
                    key,
                    userId);
            }

            await _redisCache.SetStringAsync(
                ProductCacheIndexKey,
                JsonSerializer.Serialize(cacheKeys, JsonOptions));
        }

        private async Task<HashSet<string>> GetTrackedCacheKeys()
        {
            var cacheKeysJson = await _redisCache.GetStringAsync(ProductCacheIndexKey);
            if (string.IsNullOrEmpty(cacheKeysJson))
            {
                return new HashSet<string>();
            }

            return JsonSerializer.Deserialize<HashSet<string>>(cacheKeysJson, JsonOptions) ?? new HashSet<string>();
        }
    }
}
