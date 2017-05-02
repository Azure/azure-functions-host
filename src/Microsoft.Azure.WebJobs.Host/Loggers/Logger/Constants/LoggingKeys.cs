// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    /// <summary>
    /// Keys used by the <see cref="ILogger"/> infrastructure.
    /// </summary>
    public static class LoggingKeys
    {
        /// <summary>
        /// </summary>
        public const string FullName = "FullName";

        /// <summary>
        /// </summary>
        public const string Name = "Name";

        /// <summary>
        /// </summary>
        public const string Count = "Count";

        /// <summary>
        /// </summary>
        public const string Successes = "Successes";

        /// <summary>
        /// </summary>
        public const string Failures = "Failures";

        /// <summary>
        /// </summary>
        public const string SuccessRate = "SuccessRate";

        /// <summary>
        /// </summary>
        public const string AverageDuration = "AvgDurationMs";

        /// <summary>
        /// </summary>
        public const string MaxDuration = "MaxDurationMs";

        /// <summary>
        /// </summary>
        public const string MinDuration = "MinDurationMs";

        /// <summary>
        /// </summary>
        public const string Timestamp = "Timestamp";

        /// <summary>
        /// </summary>
        public const string InvocationId = "InvocationId";

        /// <summary>
        /// </summary>
        public const string TriggerReason = "TriggerReason";

        /// <summary>
        /// </summary>
        public const string StartTime = "StartTime";

        /// <summary>
        /// </summary>
        public const string EndTime = "EndTime";

        /// <summary>
        /// </summary>
        public const string Duration = "Duration";

        /// <summary>
        /// </summary>
        public const string Succeeded = "Succeeded";

        /// <summary>
        /// </summary>
        public const string FormattedMessage = "FormattedMessage";

        /// <summary>
        /// </summary>
        public const string CategoryName = "Category";

        /// <summary>
        /// </summary>
        public const string HttpMethod = "HttpMethod";

        /// <summary>
        /// </summary>
        public const string CustomPropertyPrefix = "prop__";

        /// <summary>
        /// </summary>
        public const string ParameterPrefix = "param__";

        /// <summary>
        /// </summary>
        public const string OriginalFormat = "{OriginalFormat}";
    }
}
