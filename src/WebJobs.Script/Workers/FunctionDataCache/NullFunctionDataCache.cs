// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.FunctionDataCache
{
    public class NullFunctionDataCache : IFunctionDataCache
    {
        /// <summary>
        /// Lock to be used with any operations that access state of this cache.
        /// </summary>
        private readonly object _lock = new object();

        /// <summary>
        /// Mapping of <see cref="FunctionDataCacheKey"/> to the <see cref="SharedMemoryMetadata"/>
        /// which indicates where in shared memory the object exists.
        /// Note: This must be accessed while holding the <see cref="_lock"/> lock.
        /// </summary>
        private readonly Dictionary<FunctionDataCacheKey, SharedMemoryMetadata> _localCache = new Dictionary<FunctionDataCacheKey, SharedMemoryMetadata>();

        public bool IsEnabled { get; } = false;

        public void Dispose()
        {
        }

        public void DecrementActiveReference(FunctionDataCacheKey cacheKey)
        {
        }

        public bool TryGet(FunctionDataCacheKey cacheKey, bool isIncrementActiveReference, out SharedMemoryMetadata sharedMemoryMeta)
        {
            try
            {
                lock (_lock)
                {
                    _localCache.TryGetValue(cacheKey, out sharedMemoryMeta);
                    return false;
                }
            }
            finally
            {
            }
        }

        public bool TryPut(FunctionDataCacheKey cacheKey, SharedMemoryMetadata sharedMemoryMeta, bool isIncrementActiveReference, bool isDeleteOnFailure)
        {
            return false;
        }

        public bool TryRemove(FunctionDataCacheKey cacheKey)
        {
            return false;
        }
    }
}
