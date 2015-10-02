// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Configuration class containing settings related to log tracing.
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
        public TraceLevel ConsoleLevel { get; set; }

        /// <summary>
        /// Gets the collection of <see cref="TraceWriter"/>s that the <see cref="JobHost"/> will
        /// trace logs to. 
        /// <remarks>
        /// When <see cref="TraceWriter"/>s are added to this collection, in addition to the default
        /// Dashboard/Console logging that is done, those logs will also be routed through these
        /// <see cref="TraceWriter"/>s. This would allow you to intercept the logs that are written
        /// to the Dashboard/Console, so you can persist/inspect as needed.
        /// </remarks>
        /// </summary>
        public Collection<TraceWriter> Tracers { get; private set; }
    }
}
