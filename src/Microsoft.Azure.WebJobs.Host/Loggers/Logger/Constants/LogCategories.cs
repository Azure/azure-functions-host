// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Constant values for log categories.
    /// </summary>
    public static class LogCategories
    {
        /// <summary>
        /// The category for all logs written by the function host during startup and shutdown. This
        /// includes indexing and configuration logs.
        /// </summary>
        public const string Startup = "Host.Startup";

        /// <summary>
        /// The category for all logs written by the Singleton infrastructure.
        /// </summary>
        public const string Singleton = "Host.Singleton";

        /// <summary>
        /// The category for all logs written by the function executor.
        /// </summary>
        public const string Executor = "Host.Executor";

        /// <summary>
        /// The category for logs written by the function aggregator.
        /// </summary>
        public const string Aggregator = "Host.Aggregator";

        /// <summary>
        /// The category for function results.
        /// </summary>
        public const string Results = "Host.Results";

        /// <summary>
        /// The category for logs written from within user functions.
        /// </summary>
        public const string Function = "Function";

        /// <summary>
        /// Returns a logging category like "Host.Triggers.{triggerName}".
        /// </summary>
        /// <param name="triggerName">The trigger name.</param>
        public static string CreateTriggerCategory(string triggerName)
        {
            return $"Host.Triggers.{triggerName}";
        }
    }
}
