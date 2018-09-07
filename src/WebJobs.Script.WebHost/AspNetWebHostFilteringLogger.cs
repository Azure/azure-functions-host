// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// A logger that wraps another logger, allowing for filtering of messages before being passed on.
    /// </summary>
    public class AspNetWebHostFilteringLogger : ILogger
    {
        private readonly ILogger _logger;

        public AspNetWebHostFilteringLogger(ILogger logger)
        {
            _logger = logger;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return _logger.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _logger.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            // 1 indicates request is starting, meaning we need to remove the QueryString
            // See https://github.com/aspnet/Hosting/blob/c74d0e745824fef500ff50a1cc166c2d3037b1b8/src/Microsoft.AspNetCore.Hosting/Internal/HostingRequestStartingLog.cs
            if (eventId == 1 &&
                state is IReadOnlyList<KeyValuePair<string, object>> readOnlyState)
            {
                var d = new Dictionary<string, object>(readOnlyState);
                d.Remove("QueryString");

                string cachedString = string.Format(
                    CultureInfo.InvariantCulture,
                    "Request starting {0} {1} {2}://{3}{4}{5} {6} {7}",
                    d["Protocol"],
                    d["Method"],
                    d["Scheme"],
                    d["Host"],
                    d["PathBase"],
                    d["Path"],
                    d["ContentType"],
                    d["ContentLength"]);

                string NewFormatter(Dictionary<string, object> s, Exception e)
                {
                    return cachedString;
                }

                _logger.Log(logLevel, eventId, d, exception, NewFormatter);
            }
            else
            {
                _logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
