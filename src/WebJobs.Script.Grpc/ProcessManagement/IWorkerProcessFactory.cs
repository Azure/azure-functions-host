// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal interface IWorkerProcessFactory
    {
        // TODO: create an abstraction like Executable in the cli which wraps the process
        Process CreateWorkerProcess(WorkerContext context);
    }
}
