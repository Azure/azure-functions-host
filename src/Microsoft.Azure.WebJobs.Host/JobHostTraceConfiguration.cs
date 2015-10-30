// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Configuration class containing settings related to event tracing.
    /// <see cref="JobHostConfiguration.Tracing"/>.
    /// </summary>
    public class JobHostTraceConfiguration
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public JobHostTraceConfiguration()
        {
            ConsoleLevel = TraceLevel.Info;
            Tracers = new Collection<TraceWriter>();
        }

        /// <summary>
        /// Gets or sets the <see cref="TraceLevel"/> for console output.
        /// The default is <see cref="TraceLevel.Info"/>.
        /// </summary>
        /// <remarks>
        /// For local debugging it can be useful to increase the level to <see cref="TraceLevel.Verbose"/>
        /// to get more details, however you shouldn't run in production that way because too much output
        /// will be produced.
        /// </remarks>
        public TraceLevel ConsoleLevel { get; set; }

        /// <summary>
        /// Gets the collection of <see cref="TraceWriter"/>s that the <see cref="JobHost"/> will
        /// trace events to. 
        /// <remarks>
        /// When <see cref="TraceWriter"/>s are added to this collection, in addition to the default
        /// Dashboard/Console event logging that is done, those events will also be routed through these
        /// <see cref="TraceWriter"/>s. This would allow you to intercept the events that are written
        /// to the Dashboard/Console, so you can persist/inspect as needed.
        /// </remarks>
        /// </summary>
        public Collection<TraceWriter> Tracers { get; private set; }
    }
}
