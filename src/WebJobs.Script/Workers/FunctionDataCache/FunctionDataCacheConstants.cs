// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers.FunctionDataCache
{
    internal static class FunctionDataCacheConstants
    {
        public const string FunctionDataCacheEnabledSettingName = "FUNCTIONS_DATA_CACHE_ENABLED";
        public const string FunctionDataCacheMaximumSizeBytesSettingName = "FUNCTIONS_DATA_CACHE_MAXIMUM_SIZE_BYTES";

        /// <summary>
        /// Default size for the <see cref="IFunctionDataCache"/>.
        /// Choosing a size that is enough to give some benefit for caching smaller objects but not large enough to hog a lot of memory on the machine.
        /// </summary>
        public const long FunctionDataCacheDefaultMaximumSizeBytes = 16 * 1024 * 1024; // 16 MB
    }
}
