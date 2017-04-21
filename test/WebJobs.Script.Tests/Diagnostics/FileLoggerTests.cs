// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Internal;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class FileLoggerTests
    {
        private const string _functionShortName = "Functions.FileLoggerTest";
        private const string _functionName = "FileLoggerTest";
        private readonly ScriptHostConfiguration _config;
        private readonly string _functionLogPath;

        public FileLoggerTests()
        {
            _config = new ScriptHostConfiguration
            {
                FileLoggingMode = FileLoggingMode.Always
            };

            _functionLogPath = Path.Combine(_config.RootLogPath, "Function", _functionName);

            if (Directory.Exists(_functionLogPath))
            {
                Directory.Delete(_functionLogPath, true);
            }
        }

        [Fact]
        public void FileLogger_WritesToFiles()
        {
            FileLogger logger = new FileLogger("Tests.FileLogger", _config, null);

            using (logger.BeginScope(new Dictionary<string, object>
            {
                [ScriptConstants.LoggerFunctionNameKey] = _functionShortName
            }))
            {
                logger.LogInformation("line 1");
                logger.LogInformation("line {number}", 2);
            }

            FileLogger.FlushAllTraceWriters();

            string logFile = Directory.EnumerateFiles(_functionLogPath).Single();
            string[] fileLines = File.ReadAllLines(logFile);
            Assert.Equal(2, fileLines.Length);
            Assert.EndsWith("line 1", fileLines[0]);
            Assert.EndsWith("line 2", fileLines[1]);
        }

        [Fact]
        public void FileLogger_IgnoresTraceWriter()
        {
            FileLogger logger = new FileLogger("Tests.FileLogger", _config, null);

            TraceWriter trace = CreateUserTraceWriter();
            trace.Info("line 3");
            trace.Info("line 4");

            FileLogger.FlushAllTraceWriters();

            // The directory should not be created
            Assert.False(Directory.Exists(_functionLogPath));
        }

        protected TraceWriter CreateUserTraceWriter()
        {
            var userTraceProperties = new Dictionary<string, object>
            {
                { ScriptConstants.TracePropertyIsUserTraceKey, true }
            };

            return new LoggerTraceWriter(TraceLevel.Info, new TestLogger("Functions.User")).Apply(userTraceProperties);
        }

        private class LogMessage
        {
            public LogLevel Level { get; set; }

            public EventId EventId { get; set; }

            public IEnumerable<KeyValuePair<string, object>> State { get; set; }

            public Exception Exception { get; set; }

            public string FormattedMessage { get; set; }

            public string Category { get; set; }
        }

        private class TestLogger : ILogger
        {
            private readonly Func<string, LogLevel, bool> _filter;

            public TestLogger(string category, Func<string, LogLevel, bool> filter = null)
            {
                Category = category;
                _filter = filter;
            }

            public string Category { get; private set; }

            public IList<LogMessage> LogMessages { get; } = new List<LogMessage>();

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return _filter?.Invoke(Category, logLevel) ?? true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                LogMessages.Add(new LogMessage
                {
                    Level = logLevel,
                    EventId = eventId,
                    State = state as IEnumerable<KeyValuePair<string, object>>,
                    Exception = exception,
                    FormattedMessage = formatter(state, exception),
                    Category = Category
                });
            }
        }

        // This is a copy of the code from the SDK. It's a TraceWriter that's part of the CompositeTraceWriter
        // given to function instances for logging. Need to find a better way to test this behavior.
        private class LoggerTraceWriter : TraceWriter
        {
            private ILogger _logger;

            public LoggerTraceWriter(TraceLevel level, ILogger logger)
                : base(level)
            {
                _logger = logger;
            }

            /// <inheritdoc />
            public override void Trace(TraceEvent traceEvent)
            {
                if (traceEvent == null)
                {
                    throw new ArgumentNullException(nameof(traceEvent));
                }

                if (traceEvent.Level > Level)
                {
                    return;
                }

                LogLevel level = GetLogLevel(traceEvent.Level);
                FormattedLogValuesCollection logState = new FormattedLogValuesCollection(traceEvent.Message, null, new ReadOnlyDictionary<string, object>(traceEvent.Properties));
                _logger.Log(level, 0, logState, traceEvent.Exception, (s, e) => s.ToString());
            }

            internal static LogLevel GetLogLevel(TraceLevel traceLevel)
            {
                switch (traceLevel)
                {
                    case TraceLevel.Off:
                        return LogLevel.None;
                    case TraceLevel.Error:
                        return LogLevel.Error;
                    case TraceLevel.Warning:
                        return LogLevel.Warning;
                    case TraceLevel.Info:
                        return LogLevel.Information;
                    case TraceLevel.Verbose:
                        return LogLevel.Debug;
                    default:
                        throw new InvalidOperationException($"'{traceLevel}' is not a valid level.");
                }
            }
        }

        // Also copied from the SDK.
        private class FormattedLogValuesCollection : IReadOnlyList<KeyValuePair<string, object>>
        {
            private FormattedLogValues _formatter;
            private IReadOnlyList<KeyValuePair<string, object>> _additionalValues;

            public FormattedLogValuesCollection(string format, object[] formatValues, IReadOnlyDictionary<string, object> additionalValues)
            {
                if (formatValues != null)
                {
                    _formatter = new FormattedLogValues(format, formatValues);
                }
                else
                {
                    _formatter = new FormattedLogValues(format);
                }

                _additionalValues = additionalValues?.ToList();

                if (_additionalValues == null)
                {
                    _additionalValues = new List<KeyValuePair<string, object>>();
                }
            }

            public int Count => _formatter.Count + _additionalValues.Count;

            public KeyValuePair<string, object> this[int index]
            {
                get
                {
                    if (index < 0 || index >= Count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    if (index < _additionalValues.Count)
                    {
                        // if the index is lower, return the value from _additionalValues
                        return _additionalValues[index];
                    }
                    else
                    {
                        // if there are no more additionalValues, return from _formatter
                        return _formatter[index - _additionalValues.Count];
                    }
                }
            }

            public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
            {
                for (int i = 0; i < Count; ++i)
                {
                    yield return this[i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public override string ToString() => _formatter.ToString();
        }
    }
}
