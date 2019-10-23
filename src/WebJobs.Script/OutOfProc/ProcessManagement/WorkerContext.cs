// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.OutOfProc;

namespace Microsoft.Azure.WebJobs.Script.Rpc
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
