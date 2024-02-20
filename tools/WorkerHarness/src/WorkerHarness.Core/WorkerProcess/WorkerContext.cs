// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.WorkerProcess
{
    public sealed class WorkerContext
    {
        internal string ExecutablePath { get; }

        internal List<string> ExecutableArguments { get; }

        internal string WorkerPath { get; }

        internal List<string> WorkerArguments { get; }

        internal string WorkerId { get; }

        internal string RequestId { get; }

        internal string WorkingDirectory { get; }

        internal int MaxMessageLength { get; }

        internal Uri ServerUri { get; }

        internal WorkerContext(string executablePath,
            List<string> executableArguments,
            string workerPath,
            List<string> workerArguments,
            string workingDirectory,
            Uri serverUri)
        {
            ExecutablePath = executablePath;
            ExecutableArguments = executableArguments;
            WorkerPath = workerPath;
            WorkerArguments = workerArguments;
            WorkerId = Guid.NewGuid().ToString();
            RequestId = Guid.NewGuid().ToString();
            WorkingDirectory = workingDirectory;
            MaxMessageLength = int.MaxValue;
            ServerUri = serverUri;
        }

        internal string GetFormattedArguments()
        {
            return $" --functions-uri http://{ServerUri.Host}:{ServerUri.Port} --functions-worker-id {WorkerId} --functions-request-id {RequestId} --functions-grpc-max-message-length {MaxMessageLength}";
        }
    }
}
