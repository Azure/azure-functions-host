// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.FunctionDataCache
{
    public class NullFunctionDataCache : IFunctionDataCache
    {
        public bool IsEnabled { get; } = false;

        public void Dispose()
        {
        }

        public void DecrementActiveReference(FunctionDataCacheKey cacheKey)
        {
        }

        public bool TryGet(FunctionDataCacheKey cacheKey, bool isIncrementActiveReference, out SharedMemoryMetadata sharedMemoryMeta)
        {
            sharedMemoryMeta = null;
            return false;
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
