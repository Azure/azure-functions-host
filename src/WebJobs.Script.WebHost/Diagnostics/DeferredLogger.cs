// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class DeferredLogger : ILogger
    {
        private readonly string _category;
        private readonly BufferBlock<DeferredLogMessage> _buffer;
        private readonly IScriptWebHostEnvironment _scriptEnvironment;

        public DeferredLogger(string category, BufferBlock<DeferredLogMessage> buffer, IScriptWebHostEnvironment scriptEnvironment)
        {
            _category = category;
            _buffer = buffer;
            _scriptEnvironment = scriptEnvironment;
        }

        public IDisposable BeginScope<TState>(TState state) => DictionaryLoggerScope.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            // Don't add deferred logs if in standby mode
            return !_scriptEnvironment.InStandbyMode;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string StateFormatter(object s, Exception e)
            {
                TState castState = (TState)s;
                return formatter?.Invoke(castState, e);
            }

            DeferredLogMessage logMessage = new DeferredLogMessage
            {
                Timestamp = DateTimeOffset.UtcNow,
                Category = _category,
                LogLevel = logLevel,
                EventId = eventId,
                State = state,
                Exception = exception,
                Formatter = StateFormatter
            };

            _buffer.Post(logMessage);
        }
    }
}
