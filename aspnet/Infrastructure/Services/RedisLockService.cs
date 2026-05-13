using StackExchange.Redis;

namespace DemoAPI.Infrastructure.Services
{
    public class RedisLockService
    {
        private readonly IDatabase _db;

        public RedisLockService(IConnectionMultiplexer redis)
        {
            _db = redis.GetDatabase();
        }

        public async Task<string?> AcquireLockAsync(string key)
        {
            var token = Guid.NewGuid().ToString("N");
            
            // 嘗試以 NX (Only set the key if it does not already exist) 和 EX (Set the specified expire time, in seconds) 的方式設定鎖定鍵，過期時間為 10 秒
            // 如果設定成功，表示取得鎖定，回傳 token；如果設定失敗，表示鎖定已存在，回傳 null
            var acquired = await _db.StringSetAsync(
                key,
                token,
                TimeSpan.FromSeconds(10),
                When.NotExists
            );

            return acquired ? token : null;
        }

        public async Task<bool> ReleaseLockAsync(string key, string token)
        {
            var result = await _db.ScriptEvaluateAsync(
                """
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                end
                return 0
                """,
                new RedisKey[] { key },
                new RedisValue[] { token });

            return (int)result == 1;
        }
    }
}
