// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public class HostErrorLogger : ILogger
    {
        private readonly Action<Exception> _handleHostError;
        private Exception _lastException;

        public HostErrorLogger(Action<Exception> handleHostError)
        {
            _handleHostError = handleHostError;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (exception != null && !ShouldIgnore(exception))
            {
                _handleHostError(exception);
            }
        }

        private bool ShouldIgnore(Exception exception)
        {
            // often we may see multiple sequential error messages for the same
            // exception, so we want to skip the duplicates
            bool isDuplicate = object.ReferenceEquals(_lastException, exception);
            if (isDuplicate)
            {
                return true;
            }

            _lastException = exception;

            return false;
        }
    }
}