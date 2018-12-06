// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.Tracing;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    [EventSource(Guid = "B7AB9421-18D8-4C0A-8389-7CB78944D4A4")]
    public sealed class AzureMonitorDiagnosticLogsEventSource : ExtendedEventSource
    {
        internal static readonly AzureMonitorDiagnosticLogsEventSource Instance = new AzureMonitorDiagnosticLogsEventSource();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate")]
        [Event(65526, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 1)]
        public void RaiseFunctionsDiagnosticEventInformational(string resourceId, string operationName, string category, string location, string monitorLevel, string properties)
        {
            if (IsEnabled())
            {
                WriteEvent(65526, resourceId, operationName, category, location, monitorLevel, properties);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate")]
        [Event(65527, Level = EventLevel.Warning, Channel = EventChannel.Operational, Version = 1)]
        public void RaiseFunctionsDiagnosticEventWarning(string resourceId, string operationName, string category, string location, string monitorLevel, string properties)
        {
            if (IsEnabled())
            {
                WriteEvent(65527, resourceId, operationName, category, location, monitorLevel, properties);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate")]
        [Event(65528, Level = EventLevel.Error, Channel = EventChannel.Operational, Version = 1)]
        public void RaiseFunctionsDiagnosticEventError(string resourceId, string operationName, string category, string location, string monitorLevel, string properties)
        {
            if (IsEnabled())
            {
                WriteEvent(65528, resourceId, operationName, category, location, monitorLevel, properties);
            }
        }
    }
}
