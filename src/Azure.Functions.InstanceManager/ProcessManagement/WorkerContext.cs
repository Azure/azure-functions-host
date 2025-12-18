// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    // Arguments to start a worker process
    internal abstract class WorkerContext
    {
        public WorkerProcessArguments Arguments { get; set; } = default!;

        public string WorkerId { get; set; } = default!;

        public string RequestId { get; set; } = default!;

        public string WorkingDirectory { get; set; } = default!;

        // Environment variables to set on child process
        public IDictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();

        public abstract string GetFormattedArguments();
    }
}
