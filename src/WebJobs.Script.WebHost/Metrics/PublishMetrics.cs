// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public class PublishMetrics
    {
        /// <summary>
        /// Gets or sets a measure of the function activity for the interval.
        /// </summary>
        public long FunctionActivity { get; set; }

        /// <summary>
        /// Gets or sets the total execution duration for all functions during this interval.
        /// Gets or sets the total time duration that the instance
        /// had function activity during the interval.
        /// </summary>
        public long ExecutionTimeMS { get; set; }

        /// <summary>
        /// Gets or sets the total number of functions invocations that
        /// completed during the interval.
        /// Gets or sets the total number of functions invocations that
        /// completed during the interval.
        /// </summary>
        public long ExecutionCount { get; set; }

        /// <summary>
        /// Gets or sets the total time for the metrics interval.
        /// </summary>
        public long TotalTimeMS { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the instance is
        /// AlwaysReady.
        /// </summary>
        public bool IsAlwaysReady { get; set; }

        /// <summary>
        /// Gets or sets the instance Id.
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// Gets or sets the function group name. This can be either http, durable or
        /// the name of a function.
        /// </summary>
        public string FunctionGroup { get; set; }

        /// <summary>
        /// Gets or sets the total number of permanent host failures.
        /// </summary>
        public long AppFailureCount { get; set; }

        /// <summary>
        /// Gets or sets the total number of in-progress function invocations.
        /// </summary>
        public long ActiveInvocationCount { get; set; }

        /// <summary>
        /// Gets or sets the total number of function invocations that have started.
        /// </summary>
        public long StartedInvocationCount { get; set; }
    }
}