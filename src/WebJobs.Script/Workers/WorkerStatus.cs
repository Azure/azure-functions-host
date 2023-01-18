// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

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
        /// Gets or sets latency history.
        /// </summary>
        public IEnumerable<TimeSpan> LatencyHistory { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether worker is ready.
        /// </summary>
        public bool IsReady { get; set; }
    }
}
