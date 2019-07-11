// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class DeferredLogger : ILogger
    {
        private readonly string _category;
        private readonly ChannelWriter<DeferredLogMessage> _buffer;
        private readonly IExternalScopeProvider _scopeProvider;

        public DeferredLogger(string category, ChannelWriter<DeferredLogMessage> buffer, IExternalScopeProvider scopeProvider)
        {
            _category = category ?? throw new ArgumentNullException(nameof(category));
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

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

            IList<object> scopeList = new List<object>();
            _scopeProvider.ForEachScope((scope, s) =>
            {
                s.Add(scope);
            }, scopeList);

            DeferredLogMessage logMessage = new DeferredLogMessage
            {
                Timestamp = DateTimeOffset.UtcNow,
                Category = _category,
                LogLevel = logLevel,
                EventId = eventId,
                State = state,
                Exception = exception,
                Formatter = StateFormatter,
                Scope = scopeList
            };

            _buffer.TryWrite(logMessage);
        }
    }
}