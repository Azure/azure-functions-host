// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    internal static class HealthCheckTags
    {
        private const string Prefix = "azure.functions";

        public const string Liveness = Prefix + ".liveness";

        public const string Readiness = Prefix + ".readiness";

        public const string Configuration = Prefix + ".configuration";
    }
}
