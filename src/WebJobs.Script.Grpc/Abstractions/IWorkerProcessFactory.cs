// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    public interface IWorkerProcessFactory
    {
        Process CreateWorkerProcess(WorkerCreateContext context);
    }
}
