// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    // Arguments to start a worker process
    internal class RpcWorkerContext : WorkerContext
    {
        public Uri ServerUri { get; set; }

        public int MaxMessageLength { get; set; }

        public override string GetFormatedArguments()
        {
            return $" --host {ServerUri.Host} --port {ServerUri.Port} --workerId {WorkerId} --requestId {RequestId} --grpcMaxMessageLength {MaxMessageLength}";
        }
    }
}
