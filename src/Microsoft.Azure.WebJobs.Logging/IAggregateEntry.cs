// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Logging
{   
    /// <summary>
    /// Aggregate number of executions, per container per time bucket. 
    /// </summary>
    public interface IAggregateEntry
    {
        /// <summary>
        /// Container name this interval is for. If multiple instances are running, each has a different container name. 
        /// </summary>
        string ContainerName { get; }
        
        /// <summary>
        /// The time-bucket this interval is for. 
        /// </summary>
        long TimeBucket { get; }

        /// <summary>
        /// The time bucket represented as a datetime. 
        /// </summary>
        DateTime Time { get; }

        /// <summary>
        /// Total functions run in this interval
        /// </summary>
        int TotalRun { get; }

        /// <summary>
        /// Total functions passed in this interval
        /// </summary>
        int TotalPass { get; }

        /// <summary>
        /// Total functions failed in this interval.
        /// </summary>
        int TotalFail { get; }
    } 
}