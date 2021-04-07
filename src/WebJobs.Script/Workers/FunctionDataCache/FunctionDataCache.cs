// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.FunctionDataCache
{
    public class FunctionDataCache : IFunctionDataCache
    {
        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Lock to be used with the <see cref="_localCache"/>.
        /// </summary>
        private readonly object _localCacheLock;

        /// <summary>
        /// Mapping of <see cref="FunctionDataCacheKey"/> to the <see cref="SharedMemoryMetadata"/>
        /// which indicates where in shared memory the object exists.
        /// Note: This must be accessed while holding the <see cref="_localCacheLock"/> lock.
        /// </summary>
        private readonly Dictionary<FunctionDataCacheKey, SharedMemoryMetadata> _localCache;

        public FunctionDataCache(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<FunctionDataCache>();
            _localCacheLock = new object();
            _localCache = new Dictionary<FunctionDataCacheKey, SharedMemoryMetadata>();
        }

        public bool TryPut(FunctionDataCacheKey cacheKey, SharedMemoryMetadata sharedMemoryMeta)
        {
            lock (_localCacheLock)
            {
                if (_localCache.ContainsKey(cacheKey))
                {
                    return false;
                }

                _localCache.Add(cacheKey, sharedMemoryMeta);
                return true;
            }
        }

        public bool TryGet(FunctionDataCacheKey cacheKey, out SharedMemoryMetadata sharedMemoryMeta)
        {
            lock (_localCacheLock)
            {
                return _localCache.TryGetValue(cacheKey, out sharedMemoryMeta);
            }
        }

        public bool TryRemove(FunctionDataCacheKey cacheKey)
        {
            lock (_localCacheLock)
            {
                return _localCache.Remove(cacheKey);
            }
        }

        public void Dispose()
        {
            _logger.LogTrace("TODO");
        }
    }
}
