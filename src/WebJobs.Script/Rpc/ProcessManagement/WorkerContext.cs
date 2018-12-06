// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    // Arguments to start a worker process
    public class WorkerContext
    {
        public Uri ServerUri { get; set; }

        public WorkerProcessArguments Arguments { get; set; }

        public string WorkerId { get; set; }

        public string RequestId { get; set; }

        public string WorkingDirectory { get; set; }

        public int MaxMessageLength { get; set; }
    }
}
