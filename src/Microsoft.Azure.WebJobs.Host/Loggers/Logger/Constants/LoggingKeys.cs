// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Keys used by the <see cref="ILogger"/> infrastructure.
    /// </summary>
    public static class LoggingKeys
    {
        /// <summary>
        /// Gets the name of the key used to store the full name of the function.
        /// </summary>
        public const string FullName = "FullName";

        /// <summary>
        /// Gets the name of the key used to store the name of the function.
        /// </summary>
        public const string Name = "Name";

        /// <summary>
        /// Gets the name of the key used to store the number of invocations.
        /// </summary>
        public const string Count = "Count";

        /// <summary>
        /// Gets the name of the key used to store the success count.
        /// </summary>
        public const string Successes = "Successes";

        /// <summary>
        /// Gets the name of the key used to store the failure count.
        /// </summary>
        public const string Failures = "Failures";

        /// <summary>
        /// Gets the name of the key used to store the success rate.
        /// </summary>
        public const string SuccessRate = "SuccessRate";

        /// <summary>
        /// Gets the name of the key used to store the average duration in milliseconds.
        /// </summary>
        public const string AverageDuration = "AvgDurationMs";

        /// <summary>
        /// Gets the name of the key used to store the maximum duration in milliseconds.
        /// </summary>
        public const string MaxDuration = "MaxDurationMs";

        /// <summary>
        /// Gets the name of the key used to store the minimum duration in milliseconds.
        /// </summary>
        public const string MinDuration = "MinDurationMs";

        /// <summary>
        /// Gets the name of the key used to store the timestamp.
        /// </summary>
        public const string Timestamp = "Timestamp";

        /// <summary>
        /// Gets the name of the key used to store the function invocation id.
        /// </summary>
        public const string InvocationId = "InvocationId";

        /// <summary>
        /// Gets the name of the key used to store the trigger reason.
        /// </summary>
        public const string TriggerReason = "TriggerReason";

        /// <summary>
        /// Gets the name of the key used to store the start time.
        /// </summary>
        public const string StartTime = "StartTime";

        /// <summary>
        /// Gets the name of the key used to store the end time.
        /// </summary>
        public const string EndTime = "EndTime";

        /// <summary>
        /// Gets the name of the key used to store the duration of the function invocation.
        /// </summary>
        public const string Duration = "Duration";

        /// <summary>
        /// Gets the name of the key used to store whether the function succeeded.
        /// </summary>
        public const string Succeeded = "Succeeded";

        /// <summary>
        /// Gets the name of the key used to store the formatted message.
        /// </summary>
        public const string FormattedMessage = "FormattedMessage";

        /// <summary>
        /// Gets the name of the key used to store the category of the log message.
        /// </summary>
        public const string CategoryName = "Category";

        /// <summary>
        /// Gets the name of the key used to store the HTTP method.
        /// </summary>
        public const string HttpMethod = "HttpMethod";

        /// <summary>
        /// Gets the prefix for custom properties.
        /// </summary>
        public const string CustomPropertyPrefix = "prop__";

        /// <summary>
        /// Gets the prefix for parameters.
        /// </summary>
        public const string ParameterPrefix = "param__";

        /// <summary>
        /// Gets the name of the key used to store the original format of the log message.
        /// </summary>
        public const string OriginalFormat = "{OriginalFormat}";

        /// <summary>
        /// Gets the name of the key used to store the <see cref="LogLevel"/> of the log message.
        /// </summary>
        public const string LogLevel = "LogLevel";
    }
}
