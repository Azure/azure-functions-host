// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.OutOfProc;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    // Arguments to start a worker process
    internal class RpcWorkerContext : WorkerContext
    {
        public RpcWorkerContext(string requestId,
                int maxMessageLength,
                string workerId,
                WorkerProcessArguments workerProcessArguments,
                string workingDirectory,
                Uri serverUri,
                IDictionary<string, string> environmentVariables = null)
        {
            if (serverUri == null)
            {
                throw new ArgumentNullException(nameof(serverUri));
            }
            if (serverUri.Host == null)
            {
                throw new InvalidOperationException($"{nameof(ServerUri.Host)} is null");
            }

            RequestId = requestId;
            MaxMessageLength = LanguageWorkerConstants.DefaultMaxMessageLengthBytes;
            WorkerId = workerId;
            Arguments = workerProcessArguments;
            WorkingDirectory = workingDirectory;
            ServerUri = serverUri;
            if (environmentVariables != null)
            {
                EnvironmentVariables = environmentVariables;
            }
        }

        public Uri ServerUri { get; set; }

        public int MaxMessageLength { get; set; }

        public override string GetFormattedArguments()
        {
            return $" --host {ServerUri.Host} --port {ServerUri.Port} --workerId {WorkerId} --requestId {RequestId} --grpcMaxMessageLength {MaxMessageLength}";
        }
    }
}
