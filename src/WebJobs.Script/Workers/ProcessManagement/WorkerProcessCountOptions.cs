// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public class WorkerProcessCountOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to set FUNCTIONS_WORKER_PROCESS_COUNT to number of cpu cores on the host machine
        /// </summary>
        public bool SetProcessCountToNumberOfCpuCores { get; set; }

        /// <summary>
        /// Gets or sets number of worker processes to start. Default process count is 1
        /// </summary>
        public int ProcessCount { get; set; } = 1;

        /// <summary>
        /// Gets or sets maximum number worker processes allowed. Default max process count is 10
        /// </summary>
        public int MaxProcessCount { get; set; } = 10;

        /// <summary>
        /// Gets or sets interval between process startups. Default 2secs
        /// </summary>
        public TimeSpan ProcessStartupInterval { get; set; } = TimeSpan.FromSeconds(2);
    }
}
