using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace RedisLazyCache
{
    public class RedisCacheService : ICacheService
    {
        private readonly IDistributedCache _disributedCache;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<RedisCacheService> _logger;
        private readonly IConfiguration _config;

        private readonly ConcurrentDictionary<string, SemaphoreSlim> _lockers = new ConcurrentDictionary<string, SemaphoreSlim>();
        private bool UseDistributedCache => !string.IsNullOrEmpty(_config["ConnectionStrings:Redis"]);

        public RedisCacheService(IDistributedCache cache, IMemoryCache memoryCache, ILogger<RedisCacheService> logger, IConfiguration config)
        {
            _disributedCache = cache;
            _logger = logger;
            _config = config;
            _memoryCache = memoryCache;
        }

        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> func, int expirationInSeconds = 120)
        {
            ValidateKey(key);
            object cacheItem = new AsyncLazy<T>(async () =>
            {
                var fromCache = GetFromCache<T>(key);
                if (fromCache != null) return fromCache;

                var locker = _lockers.GetOrAdd(key, new SemaphoreSlim(1, 1));
                try
                {
                    await locker.WaitAsync().ConfigureAwait(false);

                    fromCache = GetFromCache<T>(key);
                    if (fromCache != null) return fromCache;

                    _logger.LogInformation("No cache found. Performing lazy load callback for data retrieval...");

                    var items = await func.Invoke();
                    await WriteToCache(key, items, expirationInSeconds);
                    return items;
                }
                finally
                {
                    locker.Release();
                    _lockers.TryRemove(key, out var _);
                }

            });
            var result = UnwrapAsyncLazys<T>(cacheItem);
            return await result.ConfigureAwait(false);
        }

        private T GetFromCache<T>(string key)
        {
            try
            {
                var cacheObject = _memoryCache.Get<T>(key);
                if (cacheObject != null) return cacheObject;

                if (!UseDistributedCache) return default(T);

                var byteCache = _disributedCache.Get(key);
                if (byteCache == null) return default(T);
                var content = Encoding.UTF8.GetString(byteCache);
                return JsonConvert.DeserializeObject<T>(content);
            }
            catch
            {
                return default(T);
            }
        }

        private async Task WriteToCache<T>(string key, T items, int expirationInSeconds)
        {
            try
            {
                if (!UseDistributedCache)
                {
                    _memoryCache.Set(key, items, DateTimeOffset.UtcNow.AddSeconds(expirationInSeconds));
                    return;
                }
                var toStore = JsonConvert.SerializeObject(items);
                await _disributedCache.SetAsync(key, Encoding.UTF8.GetBytes(toStore), new DistributedCacheEntryOptions { AbsoluteExpiration = DateTime.UtcNow.AddSeconds(expirationInSeconds) });
            }
            catch 
            {
                _logger.LogError("DistributedCache storage failed. Failover to MemoryCache.");
                _memoryCache.Set(key, items, DateTimeOffset.UtcNow.AddSeconds(expirationInSeconds));
            }
        }


        protected virtual Task<T> UnwrapAsyncLazys<T>(object item)
        {
            if (item is AsyncLazy<T> asyncLazy)
                return asyncLazy.Value;

            if (item is Task<T> task)
                return task;

            if (item is Lazy<T> lazy)
                return Task.FromResult(lazy.Value);

            if (item is T variable)
                return Task.FromResult(variable);

            return Task.FromResult(default(T));
        }

        protected virtual void ValidateKey(string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentOutOfRangeException(nameof(key), "Cache keys cannot be empty or whitespace");

        }
    }


}