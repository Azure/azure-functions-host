// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionTraceEvent
    {
        public DateTimeOffset Timestamp { get; set; }

        public LogLevel Level { get; set; }

        public string SubscriptionId { get; set; }

        public string AppName { get; set; }

        public string FunctionName { get; set; }

        public string EventName { get; set; }

        public string Source { get; set; }

        public string Details { get; set; }

        public string Summary { get; set; }

        public string ExceptionType { get; set; }

        public string ExceptionMessage { get; set; }

        public string FunctionInvocationId { get; set; }

        public string HostInstanceId { get; set; }

        public string ActivityId { get; set; }

        public override string ToString()
        {
            return $"[{Timestamp.ToString("HH:mm:ss.fff")}] [{Source}] {Summary}";
        }
    }
}
