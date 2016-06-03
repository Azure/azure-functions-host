// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Timeline entry from logging to track "volume" of functions executed. 
    /// </summary>
    public class FunctionVolumeTimelineEntry
    {
        /// <summary>
        /// time this entry refers to
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        /// # of function instances active during slice * size. 
        /// </summary>
        public double Volume { get; set; }

        /// <summary>
        /// total # of function instances.
        /// </summary>
        public int InstanceCounts { get; set; }
    }
}
