﻿namespace EasyCaching.Etcd
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using EasyCaching.Core;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// MemoryCaching provider.
    /// </summary>
    public partial class DefaultEtcdCachingProvider : EasyCachingAbstractProvider
    {
        /// <summary>
        /// Gets the specified cacheKey, dataRetriever and expiration async.
        /// </summary>
        /// <returns>The async.</returns>
        /// <param name="cacheKey">Cache key.</param>
        /// <param name="dataRetriever">Data retriever.</param>
        /// <param name="expiration">Expiration.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <typeparam name="T">The 1st type parameter.</typeparam>
        public override async Task<CacheValue<T>> BaseGetAsync<T>(string cacheKey, Func<Task<T>> dataRetriever, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullOrWhiteSpace(cacheKey, nameof(cacheKey));
            ArgumentCheck.NotNegativeOrZero(expiration, nameof(expiration));

            var result = await GetValAsync<T>(cacheKey);
            if (result.HasValue)
            {
                if (_options.EnableLogging)
                    _logger?.LogInformation($"Cache Hit : cachekey = {cacheKey}");

                CacheStats.OnHit();

                return result;
            }

            CacheStats.OnMiss();

            if (_options.EnableLogging)
                _logger?.LogInformation($"Cache Missed : cachekey = {cacheKey}");

            if (!AddEphemeralData($"{cacheKey}_Lock", "1", TimeSpan.FromMilliseconds(_options.LockMs)))
            {
                //wait for some ms
                await Task.Delay(_options.SleepMs, cancellationToken);
                return await GetAsync(cacheKey, dataRetriever, expiration);
            }

            try
            {
                var res = await dataRetriever();

                if (res != null || _options.CacheNulls)
                {
                    await SetAsync(cacheKey, res, expiration);
                    //remove mutex key
                  await DeleteDataAsync($"{cacheKey}_Lock");

                    return new CacheValue<T>(res, true);
                }
                else
                {
                    //remove mutex key
                   await DeleteDataAsync($"{cacheKey}_Lock");
                    return CacheValue<T>.NoValue;
                }
            }
            catch
            {
                //remove mutex key
               await DeleteDataAsync($"{cacheKey}_Lock");
                throw;
            }
        }

        /// <summary>
        /// Gets the specified cacheKey async.
        /// </summary>
        /// <returns>The async.</returns>
        /// <param name="cacheKey">Cache key.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <typeparam name="T">The 1st type parameter.</typeparam>
        public override async Task<CacheValue<T>> BaseGetAsync<T>(string cacheKey, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullOrWhiteSpace(cacheKey, nameof(cacheKey));

            var result = await GetValAsync<T>(cacheKey);

            if (result.HasValue)
            {
                if (_options.EnableLogging)
                    _logger?.LogInformation($"Cache Hit : cachekey = {cacheKey}");

                CacheStats.OnHit();

                return result;
            }
            else
            {
                if (_options.EnableLogging)
                    _logger?.LogInformation($"Cache Missed : cachekey = {cacheKey}");

                CacheStats.OnMiss();

                return CacheValue<T>.NoValue;
            }
        }

        /// <summary>
        /// Gets the count.
        /// </summary>
        /// <returns>The count.</returns>
        /// <param name="prefix">Prefix.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        public override async Task<int> BaseGetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
        {
            var dicData = await GetRangeValsAsync(prefix);
            return dicData != null ? dicData.Count : 0;
        }

        /// <summary>
        /// Gets the specified cacheKey async.
        /// </summary>
        /// <returns>The async.</returns>
        /// <param name="cacheKey">Cache key.</param>
        /// <param name="type">Object Type.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        public override async Task<object> BaseGetAsync(string cacheKey, Type type, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullOrWhiteSpace(cacheKey, nameof(cacheKey));

            var result = await GetValAsync<object>(cacheKey);

            if (result != null)
            {
                if (_options.EnableLogging)
                    _logger?.LogInformation($"Cache Hit : cachekey = {cacheKey}");

                CacheStats.OnHit();

                return result;
            }
            else
            {
                if (_options.EnableLogging)
                    _logger?.LogInformation($"Cache Missed : cachekey = {cacheKey}");

                CacheStats.OnMiss();

                return null;
            }
        }

        /// <summary>
        /// Removes the specified cacheKey async.
        /// </summary>
        /// <returns>The async.</returns>
        /// <param name="cacheKey">Cache key.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        public override async Task BaseRemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullOrWhiteSpace(cacheKey, nameof(cacheKey));

            await DeleteDataAsync(cacheKey);
        }

        /// <summary>
        /// Sets the specified cacheKey, cacheValue and expiration async.
        /// </summary>
        /// <returns>The async.</returns>
        /// <param name="cacheKey">Cache key.</param>
        /// <param name="cacheValue">Cache value.</param>
        /// <param name="expiration">Expiration.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <typeparam name="T">The 1st type parameter.</typeparam>
        public override async Task BaseSetAsync<T>(string cacheKey, T cacheValue, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullOrWhiteSpace(cacheKey, nameof(cacheKey));
            ArgumentCheck.NotNull(cacheValue, nameof(cacheValue), _options.CacheNulls);
            ArgumentCheck.NotNegativeOrZero(expiration, nameof(expiration));

            if (MaxRdSecond > 0)
            {
                var addSec = new Random().Next(1, MaxRdSecond);
                expiration = expiration.Add(TimeSpan.FromMilliseconds(addSec));
            }

            //var valExpiration = expiration.Seconds <= 1 ? expiration : TimeSpan.FromSeconds(expiration.Seconds / 2);
            //var val = new CacheValue<T>(cacheValue, true, valExpiration);
            AddEphemeralDataAsync<T>(cacheKey, cacheValue, expiration);
        }

        /// <summary>
        /// Existses the specified cacheKey async.
        /// </summary>
        /// <returns>The async.</returns>
        /// <param name="cacheKey">Cache key.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        public override async Task<bool> BaseExistsAsync(string cacheKey, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullOrWhiteSpace(cacheKey, nameof(cacheKey));

            return await GetDataExistsAsync(cacheKey);
        }

        /// <summary>
        /// Removes cached item by cachekey's prefix async.
        /// </summary>
        /// <returns>The by prefix async.</returns>
        /// <param name="prefix">Prefix.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        public override async Task BaseRemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullOrWhiteSpace(prefix, nameof(prefix));

            var count = await DeleteRangeDataAsync(prefix);

            if (_options.EnableLogging)
                _logger?.LogInformation($"RemoveByPrefixAsync : prefix = {prefix} , count = {count}");
        }

        /// <summary>
        /// Removes cached items by pattern async.
        /// </summary>
        /// <returns>The by prefix async.</returns>
        /// <param name="pattern">Pattern.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        public override async Task BaseRemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullOrWhiteSpace(pattern, nameof(pattern));

            //var searchPattern = this.ProcessSearchKeyPattern(pattern);
            //var searchKey = this.HandleSearchKeyPattern(pattern);

            //var count = await Task.Run(() => _cache.RemoveByPattern(searchKey, searchPattern), cancellationToken);

            //if (_options.EnableLogging)
            //    _logger?.LogInformation($"BaseRemoveByPatternAsync : pattern = {pattern} , count = {count}");
        }

        /// <summary>
        /// Sets all async.
        /// </summary>
        /// <returns>The all async.</returns>
        /// <param name="values">Values.</param>
        /// <param name="expiration">Expiration.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <typeparam name="T">The 1st type parameter.</typeparam>
        public override async Task BaseSetAllAsync<T>(IDictionary<string, T> values, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNegativeOrZero(expiration, nameof(expiration));
            ArgumentCheck.NotNullAndCountGTZero(values, nameof(values));

            foreach (var item in values)
            {
               await AddEphemeralDataAsync(item.Key, item.Value, expiration);
            }
        }

        /// <summary>
        /// Gets all async.
        /// </summary>
        /// <returns>The all async.</returns>
        /// <param name="cacheKeys">Cache keys.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <typeparam name="T">The 1st type parameter.</typeparam>
        public override async Task<IDictionary<string, CacheValue<T>>> BaseGetAllAsync<T>(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullAndCountGTZero(cacheKeys, nameof(cacheKeys));

            if (_options.EnableLogging)
                _logger?.LogInformation($"GetAllAsync : cacheKeys = {string.Join(",", cacheKeys)}");

            Dictionary<string, CacheValue<T>> result = new Dictionary<string, CacheValue<T>>();
            foreach (var item in cacheKeys)
            {
                var value = await BaseGetAsync<T>(item);
                result.Add(item, value);
            }
            return result;
        }


        /// <summary>
        /// Get all cacheKey by prefix async.
        /// </summary>
        /// <param name="prefix">Cache keys.</param>
        /// <param name="cancellationToken">Cache keys.</param>
        /// <returns>Get all cacheKey by prefix async.</returns>
        public override async Task<IEnumerable<string>> BaseGetAllKeysByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            if (_options.EnableLogging)
                _logger?.LogInformation("GetAllKeysAsync");

            var dicData = await GetRangeValsAsync(prefix);
            List<string> result = new List<string>();
            foreach (var item in dicData)
            {
                result.Add(item.Key);
            }
            return result;
        }

        /// <summary>
        /// Gets the by prefix async.
        /// </summary>
        /// <returns>The by prefix async.</returns>
        /// <param name="prefix">Prefix.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <typeparam name="T">The 1st type parameter.</typeparam>
        public override async Task<IDictionary<string, CacheValue<T>>> BaseGetByPrefixAsync<T>(string prefix, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullOrWhiteSpace(prefix, nameof(prefix));
            var map = new Dictionary<string, CacheValue<T>>();

            if (_options.EnableLogging)
                _logger?.LogInformation($"GetByPrefixAsync : prefix = {prefix}");

            var dicData = await GetRangeValsAsync(prefix);
            Dictionary<string, CacheValue<T>> result = new Dictionary<string, CacheValue<T>>();
            foreach (var item in dicData)
            {
                result.Add(item.Key, new CacheValue<T>(Newtonsoft.Json.JsonConvert.DeserializeObject<T>(item.Value), true));
            }
            return result;
        }

        /// <summary>
        /// Removes all async.
        /// </summary>
        /// <returns>The all async.</returns>
        /// <param name="cacheKeys">Cache keys.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        public override async Task BaseRemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullAndCountGTZero(cacheKeys, nameof(cacheKeys));

            if (_options.EnableLogging)
                _logger?.LogInformation($"RemoveAllAsync : cacheKeys = {string.Join(",", cacheKeys)}");

            foreach (var item in cacheKeys)
            {
               await DeleteDataAsync(item);
            }
        }

        /// <summary>
        /// Flush All Cached Item async.
        /// </summary>
        /// <returns>The async.</returns>
        /// <param name="cancellationToken">CancellationToken</param>
        public override async Task BaseFlushAsync(CancellationToken cancellationToken = default)
        {
            if (_options.EnableLogging)
                _logger?.LogInformation("FlushAsync");

            //_cache.Clear();
            await Task.CompletedTask;
        }

        /// <summary>
        /// Tries the set async.
        /// </summary>
        /// <returns>The set async.</returns>
        /// <param name="cacheKey">Cache key.</param>
        /// <param name="cacheValue">Cache value.</param>
        /// <param name="expiration">Expiration.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <typeparam name="T">The 1st type parameter.</typeparam>
        public override async Task<bool> BaseTrySetAsync<T>(string cacheKey, T cacheValue, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullOrWhiteSpace(cacheKey, nameof(cacheKey));
            ArgumentCheck.NotNull(cacheValue, nameof(cacheValue), _options.CacheNulls);
            ArgumentCheck.NotNegativeOrZero(expiration, nameof(expiration));

            //var val = new CacheValue<T>(cacheValue, true, expiration);
            return await AddEphemeralDataAsync(cacheKey, cacheValue, expiration);
        }

        /// <summary>
        /// Get the expiration of cache key
        /// </summary>
        /// <param name="cacheKey">cache key</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>expiration</returns>
        public override Task<TimeSpan> BaseGetExpirationAsync(string cacheKey, CancellationToken cancellationToken = default)
        {
            ArgumentCheck.NotNullOrWhiteSpace(cacheKey, nameof(cacheKey));

            // return Task.FromResult(_cache.GetExpiration(cacheKey));
            return null;
        }
    }
}
