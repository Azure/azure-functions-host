// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Describe an event for when a container is active. 
    /// </summary>
    public class ActivationEvent
    {
        /// <summary>
        /// Name of the container that's active. This may be the VM name of %COMPUTERNAME% 
        /// </summary>
        public string ContainerName { get; set; }

        /// <summary>
        /// Integral bucket value for time. Per-minute. useful for metrics. 
        /// </summary>
        public long StartTimeBucket { get; set; }

        /// <summary>
        /// Start time as a usable UTC datetime.
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Number of time buckets this event represents. 
        /// </summary>
        public long Length { get; set; }
    }
}