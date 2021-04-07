// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers.FunctionDataCache
{
    /// <summary>
    /// TODO
    /// </summary>
    public class CachableObject
    {
        public CachableObject(FunctionDataCacheKey cacheKey, object value)
        {
            CacheKey = cacheKey;
            Value = value;
        }

        public FunctionDataCacheKey CacheKey { get; private set; }

        public object Value { get; private set; }
    }
}
