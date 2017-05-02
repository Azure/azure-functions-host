// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Provides a filter for use with an <see cref="ILogger"/>.
    /// </summary>
    [System.CLSCompliant(false)]
    public class LogCategoryFilter
    {
        /// <summary>
        /// The minimum <see cref="LogLevel"/> required for logs with categories that do not match
        /// any category in <see cref="CategoryLevels"/>. 
        /// </summary>
        public LogLevel DefaultLevel { get; set; } = LogLevel.Information;

        /// <summary>
        /// A collection of filters that are used by <see cref="Filter"/> to determine if a log
        /// will be written or filtered.
        /// </summary>
        public IDictionary<string, LogLevel> CategoryLevels { get; } = new Dictionary<string, LogLevel> { };

        /// <summary>
        /// Pass this function as a filter parameter to a <see cref="ILoggerProvider"/> to enable filtering
        /// based on the specified <see cref="CategoryLevels"/>. The filter will match the longest possible key in 
        /// <see cref="CategoryLevels"/> and return true if the level is equal to or greater than the filter. If
        /// there is no match, the value of <see cref="DefaultLevel"/> is used.
        /// </summary>
        /// <param name="category">The category of the current log message.</param>
        /// <param name="level">The <see cref="LogLevel"/> of the current log message.</param>
        /// <returns>True if the level is equal to or greater than the matched filter. Otherwise, false.</returns>
        public bool Filter(string category, LogLevel level)
        {
            // find the longest loglevel that matches the category
            IEnumerable<string> matches = CategoryLevels.Keys.Where(k => category.StartsWith(k, System.StringComparison.CurrentCulture));
            string longestMatch = matches?.Max();

            LogLevel requestedLevel;
            if (string.IsNullOrEmpty(longestMatch))
            {
                requestedLevel = DefaultLevel;
            }
            else
            {
                requestedLevel = CategoryLevels[longestMatch];
            }

            return level >= requestedLevel;
        }
    }
}
