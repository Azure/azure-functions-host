// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

/// <summary>
/// Reports request tracing failures without calling a potentially failing logger provider.
/// </summary>
internal static class HttpRequestTraceDiagnostics
{
    internal static bool IsRecoverable(Exception exception)
    {
        return !exception.IsFatal() &&
            (exception is not AggregateException aggregate || aggregate.InnerExceptions.All(IsRecoverable));
    }

    internal static void ReportFailure(string operation, Exception exception)
    {
        Type exceptionType = exception.GetType();
        Events.Log.RequestTracingFailed(operation, exceptionType.FullName ?? exceptionType.Name);
    }

    [EventSource(Name = $"{ScriptConstants.HostEventSourcePrefix}{nameof(HttpRequestTraceDiagnostics)}")]
    private sealed class Events : EventSource
    {
        internal static readonly Events Log = new();

        private Events()
        {
        }

        [Event(1, Message = "HTTP request tracing failed during {0}: {1}", Level = EventLevel.Warning)]
        public void RequestTracingFailed(string operation, string exceptionType)
        {
            WriteEvent(1, operation, exceptionType);
        }
    }
}
