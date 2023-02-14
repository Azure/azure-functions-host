// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class FunctionsHostingConfigOptionsExtensions
    {
        public static bool IsFunctionsWorkerDynamicConcurrencyEnabled(this FunctionsHostingConfigOptions hostingConfigOptions)
        {
            return hostingConfigOptions.GetFeature(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled) == "1";
        }
    }
}
