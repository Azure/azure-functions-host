// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public static class KuduConstants
    {
        internal const string DataPath = "data";
        internal const string DummyRazorExtension = ".kudu777";
        internal const string Function = "function";
        internal const string Functions = "functions";
        internal const string FunctionsConfigFile = "function.json";
        internal const string FunctionsHostConfigFile = "host.json";
        internal const string Secrets = "secrets";
        internal const string SampleData = "sampledata";
        internal const string FunctionsPortal = "FunctionsPortal";
        internal const string HttpHost = "HTTP_HOST";
        internal const string SiteRestrictedJWT = "X-MS-SITE-RESTRICTED-JWT";
    }
}