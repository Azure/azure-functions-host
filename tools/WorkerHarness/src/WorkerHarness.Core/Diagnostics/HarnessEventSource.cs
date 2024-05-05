// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.Tracing;

namespace WorkerHarness.Core.Diagnostics
{
    // Use 9000-9999 for events from FunctionsNetHost.
    [EventSource(Name = Constants.EventSourceName, Guid = Constants.EventSourceGuid)]
    public sealed class HarnessEventSource : EventSource
    {
        [Event(9001)]
        public void AppStarted()
        {
            WriteEvent(9001);
        }

        [Event(9002)]
        public void ColdStartRequestStart()
        {
            if (IsEnabled())
            {
                WriteEvent(9002);
            }
        }

        [Event(9003)]
        public void ColdStartRequestStop(string statusCode)
        {
            if (IsEnabled())
            {
                WriteEvent(9003, statusCode);
            }
        }

        public static readonly HarnessEventSource Log = new();
    }
}
