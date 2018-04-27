using System;
using System.Threading.Tasks;

namespace RedisLazyCache
{
    public interface ICacheService
    {
        Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> func, int expirationInSeconds = 120);
    }
}
