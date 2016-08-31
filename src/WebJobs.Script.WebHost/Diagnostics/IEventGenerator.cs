// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public interface IEventGenerator
    {
        void LogFunctionsEventVerbose(string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary);

        void LogFunctionsEventInfo(string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary);

        void LogFunctionsEventWarning(string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary);

        void LogFunctionsEventError(string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary);

        void LogFunctionsMetrics(string subscriptionId, string appName, string eventName, long average, long minimum, long maximum, long count);
    }
}
