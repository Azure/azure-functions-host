// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public interface ISystemEventGenerator
    {
        void LogEvent(TraceLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, Exception exception = null);

        void LogMetric(string subscriptionId, string appName, string eventName, long average, long minimum, long maximum, long count);
    }
}
