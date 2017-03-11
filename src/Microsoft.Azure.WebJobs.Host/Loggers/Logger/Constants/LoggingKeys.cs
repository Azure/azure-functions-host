// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal static class LoggingKeys
    {
        // These are publicly visible as property names or prefixes
        public const string FullName = "FullName";
        public const string Name = "Name";
        public const string Count = "Count";
        public const string Successes = "Successes";
        public const string Failures = "Failures";
        public const string SuccessRate = "SuccessRate";
        public const string AvgDuration = "AvgDurationMs";
        public const string MaxDuration = "MaxDurationMs";
        public const string MinDuration = "MinDurationMs";
        public const string Timestamp = "Timestamp";
        public const string InvocationId = "InvocationId";
        public const string TriggerReason = "TriggerReason";
        public const string StartTime = "StartTime";
        public const string EndTime = "EndTime";
        public const string Duration = "Duration";
        public const string Succeeded = "Succeeded";
        public const string FormattedMessage = "FormattedMessage";
        public const string CategoryName = "Category";
        public const string HttpMethod = "HttpMethod";
        public const string CustomPropertyPrefix = "prop__";
        public const string ParameterPrefix = "param__";
        public const string OriginalFormat = "{OriginalFormat}";
    }
}
