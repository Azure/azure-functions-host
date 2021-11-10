// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    internal class MockCacheAwareReadObject : ICacheAwareReadObject
    {
        private readonly IFunctionDataCache _functionDataCache;

        public MockCacheAwareReadObject(FunctionDataCacheKey cacheKey, SharedMemoryMetadata cacheObject, IFunctionDataCache functionDataCache)
        {
            _functionDataCache = functionDataCache;
            CacheKey = cacheKey;
            CacheObject = cacheObject;
            IsCacheHit = true;
        }

        public MockCacheAwareReadObject(FunctionDataCacheKey cacheKey, Stream blobStream, IFunctionDataCache functionDataCache)
        {
            _functionDataCache = functionDataCache;
            CacheKey = cacheKey;
            BlobStream = blobStream;
            IsCacheHit = false;
        }

        public bool IsCacheHit { get; private set; }

        public FunctionDataCacheKey CacheKey { get; private set; }

        public SharedMemoryMetadata CacheObject { get; private set; }

        public Stream BlobStream { get; private set; }

        public bool TryPutToCache(SharedMemoryMetadata cacheObject, bool isIncrementActiveReference)
        {
            if (IsCacheHit)
            {
                return false;
            }

            return _functionDataCache.TryPut(CacheKey, cacheObject, isIncrementActiveReference, isDeleteOnFailure: false);
        }

        public void Dispose()
        {
            return;
        }
    }
}
