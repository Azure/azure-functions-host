// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    [EventSource(Guid = "a7044dd6-c8ef-4980-858c-942d972b6250")]
    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1313:ParameterNamesMustBeginWithLowerCaseLetter", Justification = "MDS columns names need to Pascal case", Scope = "member", Target = "~M:Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.EventGenerator.FunctionsSystemLogsEventSource)")]
    public sealed class FunctionsSystemLogsEventSource : ExtendedEventSource
    {
        internal static readonly FunctionsSystemLogsEventSource Instance = new FunctionsSystemLogsEventSource();

        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", Justification = "MDS columns names are Pascal Cased")]
        [SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate")]
        [Event(65520, Level = EventLevel.Verbose, Channel = EventChannel.Operational, Version = 4)]
        public void RaiseFunctionsEventVerbose(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp, string FunctionInvocationId, string HostInstanceId, string RuntimeSiteName)
        {
            if (IsEnabled())
            {
                WriteEvent(65520, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp, FunctionInvocationId, HostInstanceId, RuntimeSiteName);
            }
        }

        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly")]
        [SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate")]
        [Event(65521, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 4)]
        public void RaiseFunctionsEventInfo(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp, string FunctionInvocationId, string HostInstanceId, string RuntimeSiteName)
        {
            if (IsEnabled())
            {
                WriteEvent(65521, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp, FunctionInvocationId, HostInstanceId, RuntimeSiteName);
            }
        }

        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly")]
        [SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate")]
        [Event(65522, Level = EventLevel.Warning, Channel = EventChannel.Operational, Version = 4)]
        public void RaiseFunctionsEventWarning(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp, string FunctionInvocationId, string HostInstanceId, string RuntimeSiteName)
        {
            if (IsEnabled())
            {
                WriteEvent(65522, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp, FunctionInvocationId, HostInstanceId, RuntimeSiteName);
            }
        }

        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly")]
        [SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate")]
        [Event(65523, Level = EventLevel.Error, Channel = EventChannel.Operational, Version = 4)]
        public void RaiseFunctionsEventError(string SubscriptionId, string AppName, string FunctionName, string EventName, string Source, string Details, string Summary, string HostVersion, string EventTimestamp, string InnerExceptionType, string InnerExceptionMessage, string FunctionInvocationId, string HostInstanceId, string RuntimeSiteName)
        {
            if (IsEnabled())
            {
                WriteEvent(65523, SubscriptionId, AppName, FunctionName, EventName, Source, Details, Summary, HostVersion, EventTimestamp, InnerExceptionType, InnerExceptionMessage, FunctionInvocationId, HostInstanceId, RuntimeSiteName);
            }
        }

        [Event(65524, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 5)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly")]
        [SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate")]
        public void LogFunctionMetricEvent(string SubscriptionId, string AppName, string FunctionName, string EventName, long Average, long Minimum, long Maximum, long Count, string HostVersion, string EventTimestamp, string Data, string RuntimeSiteName)
        {
            if (IsEnabled())
            {
                WriteEvent(65524, SubscriptionId, AppName, FunctionName, EventName, Average, Minimum, Maximum, Count, HostVersion, EventTimestamp, Data, RuntimeSiteName);
            }
        }

        public void SetActivityId(string activityId)
        {
            Guid guid;
            if (!string.IsNullOrEmpty(activityId) && Guid.TryParse(activityId, out guid))
            {
                SetCurrentThreadActivityId(guid);
            }
        }
    }
}