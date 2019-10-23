// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc.Http
{
    // Arguments to start a worker process
    internal class HttpWorkerContext : WorkerContext
    {
        public int Port { get; set; }

        public override string GetFormattedArguments()
        {
           return $" --workerId {WorkerId} --port {Port}";
        }
    }
}
