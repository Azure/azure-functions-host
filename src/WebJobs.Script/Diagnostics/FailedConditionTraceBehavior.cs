// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.


namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Specifies how the <see cref="ConditionalTraceWriter"/> should behave when the
    /// condition check fails.
    /// </summary>
    public enum FailedConditionTraceBehavior
    {
        /// <summary>
        /// Trace the event using <see cref="TraceLevel.Verbose"/>.
        /// </summary>
        TraceAsVerbose,

        /// <summary>
        /// Ignore the event. 
        /// </summary>
        IgnoreTraceEvent
    }
}
