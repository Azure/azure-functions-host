// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Scale;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    /// <summary>
    /// Represents the health status of a worker.
    /// </summary>
    public class WorkerStatus
    {
        /// <summary>
        /// Gets or sets the current latency for worker channel.
        /// </summary>
        public TimeSpan Latency { get; set; }

        /// <summary>
        /// Gets or sets the process statistics for the worker process.
        /// </summary>
        public ProcessStats ProcessStats { get; set; }
    }
}
