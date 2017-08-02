// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class LoggerExtensionsTests
    {
        private Guid _invocationId = Guid.NewGuid();
        private DateTime _startTime = DateTime.Now;
        private DateTime _endTime;
        private string _triggerReason = "new queue message";
        private string _functionShortName = "TestFunction";
        private string _functionFullName = "Functions.TestFunction";
        private TimeSpan _duration = TimeSpan.FromMilliseconds(450);
        private IDictionary<string, string> _arguments;

        public LoggerExtensionsTests()
        {
            _endTime = _startTime.AddMilliseconds(450);
            _arguments = new Dictionary<string, string>
            {
                ["queueMessage"] = "my message",
                ["anotherParam"] = "some value"
            };
        }

        [Fact]
        public void LogFunctionResult_Succeeded_CreatesCorrectState()
        {
            int logCount = 0;
            ILogger logger = CreateMockLogger<FormattedLogValuesCollection>((l, e, o, ex, f) =>
            {
                logCount++;
                Assert.Equal(LogLevel.Information, l);
                Assert.Equal(0, e);
                Assert.Null(ex);
                Assert.Equal($"Executed '{_functionFullName}' (Succeeded, Id={_invocationId})", f(o, ex));

                var payload = VerifyResultDefaultsAndConvert(o);
                Assert.True((bool)payload[LogConstants.SucceededKey]);
                Assert.Equal("Executed '{FullName}' (Succeeded, Id={InvocationId})", payload[LogConstants.OriginalFormatKey]);
            });

            var result = CreateDefaultInstanceLogEntry();

            logger.LogFunctionResult(result);

            Assert.Equal(1, logCount);
        }

        [Fact]
        public void LogFunctionResult_Failed_CreatesCorrectState()
        {
            int logCount = 0;
            ILogger logger = CreateMockLogger<FormattedLogValuesCollection>((l, e, o, ex, f) =>
            {
                logCount++;
                Assert.Equal(LogLevel.Error, l);
                Assert.Equal(0, e);
                Assert.NotNull(ex);
                Assert.IsType<FunctionInvocationException>(ex);
                Assert.Equal($"Executed '{_functionFullName}' (Failed, Id={_invocationId})", f(o, ex));

                var payload = VerifyResultDefaultsAndConvert(o);
                Assert.False((bool)payload[LogConstants.SucceededKey]);
                Assert.Equal("Executed '{FullName}' (Failed, Id={InvocationId})", payload[LogConstants.OriginalFormatKey]);
            });

            var fex = new FunctionInvocationException("Failed");
            var result = CreateDefaultInstanceLogEntry(fex);

            logger.LogFunctionResult(result);

            Assert.Equal(1, logCount);
        }

        [Fact]
        public void LogFunctionResultAggregate_CreatesCorrectState()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            int logCount = 0;
            ILogger logger = CreateMockLogger<FormattedLogValuesCollection>((l, e, o, ex, f) =>
            {
                logCount++;
                Assert.Equal(LogLevel.Information, l);
                Assert.Equal(0, e);
                Assert.Null(ex);

                // nothing logged
                Assert.Equal(string.Empty, f(o, ex));

                // convert to dictionary                
                var payload = o.ToDictionary(k => k.Key, v => v.Value);

                Assert.Equal(10, payload.Count);
                Assert.Equal(_functionShortName, payload[LogConstants.NameKey]);
                Assert.Equal(4, payload[LogConstants.FailuresKey]);
                Assert.Equal(116, payload[LogConstants.SuccessesKey]);
                Assert.Equal(TimeSpan.FromMilliseconds(200), (TimeSpan)payload[LogConstants.MinDurationKey]);
                Assert.Equal(TimeSpan.FromMilliseconds(2180), (TimeSpan)payload[LogConstants.MaxDurationKey]);
                Assert.Equal(TimeSpan.FromMilliseconds(340), (TimeSpan)payload[LogConstants.AverageDurationKey]);
                Assert.Equal(now, payload[LogConstants.TimestampKey]);
                Assert.Equal(120, payload[LogConstants.CountKey]);
                Assert.Equal(96.67, payload[LogConstants.SuccessRateKey]);

                // {OriginalFormat} is still added, even though it is empty
                Assert.Equal(string.Empty, payload[LogConstants.OriginalFormatKey]);
            });

            var resultAggregate = new FunctionResultAggregate
            {
                Name = _functionShortName,
                Failures = 4,
                Successes = 116,
                MinDuration = TimeSpan.FromMilliseconds(200),
                MaxDuration = TimeSpan.FromMilliseconds(2180),
                AverageDuration = TimeSpan.FromMilliseconds(340),
                Timestamp = now
            };

            logger.LogFunctionResultAggregate(resultAggregate);

            Assert.Equal(1, logCount);
        }

        private FunctionInstanceLogEntry CreateDefaultInstanceLogEntry(Exception ex = null)
        {
            return new FunctionInstanceLogEntry
            {
                FunctionName = _functionFullName,
                LogName = _functionShortName,
                FunctionInstanceId = _invocationId,
                StartTime = _startTime,
                EndTime = _endTime,
                LogOutput = "a bunch of output that we will not forward", // not used here -- this is all Traced
                TriggerReason = _triggerReason,
                ParentId = Guid.NewGuid(), // we do not track this
                ErrorDetails = null, // we do not use this -- we pass the exception in separately
                Arguments = _arguments,
                Duration = _duration,
                Exception = ex
            };
        }

        private IDictionary<string, object> VerifyResultDefaultsAndConvert<TState>(TState state)
        {
            var enumerable = state as IEnumerable<KeyValuePair<string, object>>;
            Assert.NotNull(enumerable);

            var payload = enumerable.ToDictionary(k => k.Key, v => v.Value);

            Assert.Equal(11, payload.Count);
            Assert.Equal(_functionFullName, payload[LogConstants.FullNameKey]);
            Assert.Equal(_functionShortName, payload[LogConstants.NameKey]);
            Assert.Equal(_invocationId, payload[LogConstants.InvocationIdKey]);
            Assert.Equal(_startTime, payload[LogConstants.StartTimeKey]);
            Assert.Equal(_endTime, payload[LogConstants.EndTimeKey]);
            Assert.Equal(_duration, payload[LogConstants.DurationKey]);
            Assert.Equal(_triggerReason, payload[LogConstants.TriggerReasonKey]);

            // verify default arguments were passed with prefix
            var args = payload.Where(kvp => kvp.Value is string && kvp.Key.ToString().StartsWith(LogConstants.ParameterPrefix));
            Assert.Equal(_arguments.Count, args.Count());
            foreach (var arg in _arguments)
            {
                var payloadKey = LogConstants.ParameterPrefix + arg.Key;
                Assert.Equal(arg.Value, args.Single(kvp => kvp.Key == payloadKey).Value.ToString());
            }

            return payload;
        }

        private static ILogger CreateMockLogger<TState>(Action<LogLevel, EventId, TState, Exception, Func<TState, Exception, string>> callback)
        {
            var mockLogger = new Mock<ILogger>(MockBehavior.Strict);
            mockLogger
                .Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<TState>(), It.IsAny<Exception>(), It.IsAny<Func<TState, Exception, string>>()))
                .Callback<LogLevel, EventId, TState, Exception, Func<TState, Exception, string>>((l, e, o, ex, f) =>
                {
                    callback(l, e, o, ex, f);
                });

            return mockLogger.Object;
        }
    }
}
