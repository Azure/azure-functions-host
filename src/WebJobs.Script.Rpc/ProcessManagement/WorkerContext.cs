// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    // Arguments to start a worker process
    public abstract class WorkerContext
    {
        public WorkerProcessArguments Arguments { get; set; }

        public string WorkerId { get; set; }

        public string RequestId { get; set; }

        public string WorkingDirectory { get; set; }

        // Environment variables to set on child process
        public IDictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();

        public abstract string GetFormattedArguments();
    }
}
