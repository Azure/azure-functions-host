// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public interface IRpcWorkerChannelFactory
    {
        ILanguageWorkerChannel Create(string scriptRootPath, string language, IMetricsLogger metricsLogger, int attemptCount, IOptions<ManagedDependencyOptions> managedDependencyOptions = null);
    }
}
