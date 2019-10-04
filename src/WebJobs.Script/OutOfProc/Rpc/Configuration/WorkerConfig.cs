// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.OutOfProc;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class WorkerConfig
    {
        public RpcWorkerDescription Description { get; set; }

        public WorkerProcessArguments Arguments { get; set; }
    }
}
