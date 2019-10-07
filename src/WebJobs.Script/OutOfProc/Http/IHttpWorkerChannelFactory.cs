// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc
{
    public interface IHttpWorkerChannelFactory
    {
        IHttpWorkerChannel Create(string scriptRootPath, IMetricsLogger metricsLogger, int attemptCount);
    }
}
