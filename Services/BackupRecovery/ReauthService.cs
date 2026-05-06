using System;
using Microsoft.Extensions.Caching.Memory;

namespace DMS_CPMS.Services.BackupRecovery
{
    public interface IReauthService
    {
        void MarkReauthenticated(string userId, string purpose, TimeSpan validFor);
        bool IsReauthenticated(string userId, string purpose);
        void Clear(string userId, string purpose);
    }

    public sealed class ReauthService : IReauthService
    {
        private readonly IMemoryCache _cache;
        public ReauthService(IMemoryCache cache) => _cache = cache;

        public void MarkReauthenticated(string userId, string purpose, TimeSpan validFor)
        {
            var key = Key(userId, purpose);
            _cache.Set(key, true, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = validFor
            });
        }

        public bool IsReauthenticated(string userId, string purpose)
            => _cache.TryGetValue(Key(userId, purpose), out var v) && v is true;

        public void Clear(string userId, string purpose) => _cache.Remove(Key(userId, purpose));

        private static string Key(string userId, string purpose) => $"reauth:{purpose}:{userId}";
    }
}

