// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public interface IWorkerProcessFactory
    {
        // TODO: create an abstraction like Executable in the cli which wraps the process
        Process CreateWorkerProcess(WorkerCreateContext context);
    }
}
