// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    public static class CapabilitySourceNames
    {
        /// <summary>
        /// Source name for configuration-based capabilities.
        /// </summary>
        public const string ConfigSource = "config";

        /// <summary>
        /// Source name for host-based capabilities.
        /// </summary>
        public const string HostSource = "host";

        /// <summary>
        /// Source name for worker-based capabilities.
        /// </summary>
        public const string WorkerSource = "worker";

        /// <summary>
        /// Source name for extension-based capabilities.
        /// </summary>
        public const string ExtensionSource = "extension";
    }
}