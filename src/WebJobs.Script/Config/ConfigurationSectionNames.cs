﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public static class ConfigurationSectionNames
    {
        public const string WebHost = "AzureFunctionsWebHost";
        public const string JobHost = "AzureFunctionsJobHost";
        public const string Logging = "logging";
        public const string Aggregator = "aggregator";
        public const string HealthMonitor = "healthMonitor";
        public const string HostIdPath = WebHost + ":hostid";
        public const string ExtensionBundle = "extensionBundle";
        public const string ManagedDependency = "managedDependency";
    }
}
