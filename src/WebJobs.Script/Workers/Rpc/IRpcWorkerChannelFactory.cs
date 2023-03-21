// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public interface IRpcWorkerChannelFactory
    {
        IRpcWorkerChannel Create(string scriptRootPath, string language, IMetricsLogger metricsLogger, int attemptCount, IEnumerable<RpcWorkerConfig> workerConfigs);
    }
}
