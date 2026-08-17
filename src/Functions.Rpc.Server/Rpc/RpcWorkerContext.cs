// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
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
            MaxMessageLength = RpcWorkerConstants.DefaultMaxMessageLengthBytes;
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
            // Adding a second copy of the commandline arguments with the "functions-" prefix to prevent any conflicts caused by the existing generic names.
            // Language workers are advised to use the "functions-" prefix ones and if not present fallback to existing ones.

            return $" --host {ServerUri.Host} --port {ServerUri.Port} --workerId {WorkerId} --requestId {RequestId} --grpcMaxMessageLength {MaxMessageLength} --functions-uri {ServerUri.AbsoluteUri} --functions-worker-id {WorkerId} --functions-request-id {RequestId} --functions-grpc-max-message-length {MaxMessageLength}";
        }
    }
}
