// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    [EventSource(Guid = "08D0D743-5C24-43F9-9723-98277CEA5F9B")]
    public sealed class FunctionsEventSource : ExtendedEventSource
    {
        internal static readonly FunctionsEventSource Instance = new FunctionsEventSource();

        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "Ms")]
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentfiersShouldBeSpelledCorrectly", MessageId = "Ms")]
        [Event(57907, Level = EventLevel.Informational, Channel = EventChannel.Operational, Keywords = FunctionsKeywords.DefaultMetricsKeyword)]
        public void LogFunctionExecutionAggregateEvent(string siteName, string functionName, ulong executionTimeInMs, ulong functionStartedCount, ulong functionCompletedCount, ulong functionFailedCount)
        {
            if (IsEnabled())
            {
                WriteEvent(57907, siteName, functionName, executionTimeInMs, functionStartedCount, functionCompletedCount, functionFailedCount);
            }
        }

        [Event(57908, Level = EventLevel.Informational, Channel = EventChannel.Operational, Keywords = FunctionsKeywords.DefaultMetricsKeyword)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly")]
        public void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
            if (IsEnabled())
            {
                WriteEvent(57908, siteName, functionName, inputBindings, outputBindings, scriptType, isDisabled);
            }
        }

        [Event(57909, Level = EventLevel.Informational, Channel = EventChannel.Operational, Keywords = FunctionsKeywords.DefaultMetricsKeyword)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly")]
        public void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, ulong executionTimeSpan, bool success)
        {
            if (IsEnabled())
            {
                WriteEvent(57909, executionId, siteName, concurrency, functionName, invocationId, executionStage, executionTimeSpan, success);
            }
        }

        internal class FunctionsKeywords
        {
            public const EventKeywords DefaultMetricsKeyword = unchecked((EventKeywords)0x8000000000000000);
        }
    }
}