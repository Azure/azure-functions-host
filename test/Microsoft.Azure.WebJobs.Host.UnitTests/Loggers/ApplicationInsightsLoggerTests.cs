// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class ApplicationInsightsLoggerTests
    {
        private readonly Guid _invocationId = Guid.NewGuid();
        private readonly DateTime _startTime = DateTime.UtcNow;
        private readonly DateTime _endTime;
        private readonly string _triggerReason = "new queue message";
        private readonly string _functionFullName = "Functions.TestFunction";
        private readonly string _functionShortName = "TestFunction";
        private readonly IDictionary<string, string> _arguments;
        private readonly TestTelemetryChannel _channel = new TestTelemetryChannel();
        private readonly string defaultIp = "0.0.0.0";
        private readonly TelemetryClient _client;
        private readonly int durationMs = 450;

        public ApplicationInsightsLoggerTests()
        {
            _endTime = _startTime.AddMilliseconds(durationMs);
            _arguments = new Dictionary<string, string>
            {
                ["queueMessage"] = "my message",
                ["anotherParam"] = "some value"
            };

            TelemetryConfiguration config = new TelemetryConfiguration
            {
                TelemetryChannel = _channel,
                InstrumentationKey = "some key"
            };

            // Add the same initializers that we use in the product code
            DefaultTelemetryClientFactory.AddInitializers(config);

            _client = new TelemetryClient(config);
        }

        [Fact]
        public void LogFunctionResult_Succeeded_SendsCorrectTelemetry()
        {
            var result = CreateDefaultInstanceLogEntry();
            ILogger logger = CreateLogger(LogCategories.Results);

            using (logger.BeginFunctionScope(CreateFunctionInstance(_invocationId)))
            {
                logger.LogFunctionResult(_functionShortName, result, TimeSpan.FromMilliseconds(durationMs));
            }

            RequestTelemetry telemetry = _channel.Telemetries.Single() as RequestTelemetry;

            Assert.Equal(_invocationId.ToString(), telemetry.Id);
            Assert.Equal(_invocationId.ToString(), telemetry.Context.Operation.Id);
            Assert.Equal(_functionShortName, telemetry.Name);
            Assert.Equal(_functionShortName, telemetry.Context.Operation.Name);
            Assert.Equal(defaultIp, telemetry.Context.Location.Ip);
            Assert.Equal(LogCategories.Results, telemetry.Properties[LoggingKeys.CategoryName]);
            Assert.Equal(LogLevel.Information.ToString(), telemetry.Properties[LoggingKeys.LogLevel]);
            // TODO: Beef up validation to include properties
        }

        [Fact]
        public void LogFunctionResult_Failed_SendsCorrectTelemetry()
        {
            var result = CreateDefaultInstanceLogEntry();
            FunctionInvocationException fex = new FunctionInvocationException("Failed");
            ILogger logger = CreateLogger(LogCategories.Results);

            using (logger.BeginFunctionScope(CreateFunctionInstance(_invocationId)))
            {
                logger.LogFunctionResult(_functionShortName, result, TimeSpan.FromMilliseconds(durationMs), fex);
            }

            // Errors log an associated Exception
            RequestTelemetry requestTelemetry = _channel.Telemetries.OfType<RequestTelemetry>().Single();
            ExceptionTelemetry exceptionTelemetry = _channel.Telemetries.OfType<ExceptionTelemetry>().Single();

            Assert.Equal(2, _channel.Telemetries.Count);
            Assert.Equal(_invocationId.ToString(), requestTelemetry.Id);
            Assert.Equal(_invocationId.ToString(), requestTelemetry.Context.Operation.Id);
            Assert.Equal(_functionShortName, requestTelemetry.Name);
            Assert.Equal(_functionShortName, requestTelemetry.Context.Operation.Name);
            Assert.Equal(defaultIp, requestTelemetry.Context.Location.Ip);
            Assert.Equal(LogCategories.Results, requestTelemetry.Properties[LoggingKeys.CategoryName]);
            Assert.Equal(LogLevel.Error.ToString(), requestTelemetry.Properties[LoggingKeys.LogLevel]);
            // TODO: Beef up validation to include properties

            // Exception needs to have associated id
            Assert.Equal(_invocationId.ToString(), exceptionTelemetry.Context.Operation.Id);
            Assert.Equal(_functionShortName, exceptionTelemetry.Context.Operation.Name);
            Assert.Same(fex, exceptionTelemetry.Exception);
            Assert.Equal(LogCategories.Results, exceptionTelemetry.Properties[LoggingKeys.CategoryName]);
            Assert.Equal(LogLevel.Error.ToString(), exceptionTelemetry.Properties[LoggingKeys.LogLevel]);
            // TODO: Beef up validation to include properties
        }

        [Fact]
        public void LogFunctionAggregate_SendsCorrectTelemetry()
        {
            DateTime now = DateTime.UtcNow;
            var resultAggregate = new FunctionResultAggregate
            {
                Name = _functionFullName,
                Failures = 4,
                Successes = 116,
                MinDuration = TimeSpan.FromMilliseconds(200),
                MaxDuration = TimeSpan.FromMilliseconds(2180),
                AverageDuration = TimeSpan.FromMilliseconds(340),
                Timestamp = now
            };

            ILogger logger = CreateLogger(LogCategories.Aggregator);
            logger.LogFunctionResultAggregate(resultAggregate);

            IEnumerable<MetricTelemetry> metrics = _channel.Telemetries.Cast<MetricTelemetry>();
            // turn them into a dictionary so we can easily validate
            IDictionary<string, MetricTelemetry> metricDict = metrics.ToDictionary(m => m.Name, m => m);

            Assert.Equal(7, metricDict.Count);

            ValidateMetric(metricDict[$"{_functionFullName} {LoggingKeys.Failures}"], 4, LogLevel.Information);
            ValidateMetric(metricDict[$"{_functionFullName} {LoggingKeys.Successes}"], 116, LogLevel.Information);
            ValidateMetric(metricDict[$"{_functionFullName} {LoggingKeys.MinDuration}"], 200, LogLevel.Information);
            ValidateMetric(metricDict[$"{_functionFullName} {LoggingKeys.MaxDuration}"], 2180, LogLevel.Information);
            ValidateMetric(metricDict[$"{_functionFullName} {LoggingKeys.AverageDuration}"], 340, LogLevel.Information);
            ValidateMetric(metricDict[$"{_functionFullName} {LoggingKeys.SuccessRate}"], 96.67, LogLevel.Information);
            ValidateMetric(metricDict[$"{_functionFullName} {LoggingKeys.Count}"], 120, LogLevel.Information);
        }

        private static void ValidateMetric(MetricTelemetry metric, double expectedValue, LogLevel expectedLevel, string expectedCategory = LogCategories.Aggregator)
        {
            Assert.Equal(expectedValue, metric.Value);
            Assert.Equal(2, metric.Properties.Count);
            Assert.Equal(expectedCategory, metric.Properties[LoggingKeys.CategoryName]);
            Assert.Equal(expectedLevel.ToString(), metric.Properties[LoggingKeys.LogLevel]);
        }

        [Fact]
        public void LogFunctionResult_HttpRequest_SendsCorrectTelemetry()
        {
            // If the scope has an HttpRequestMessage, we'll use the proper values
            // for the RequestTelemetry
            DateTime now = DateTime.UtcNow;
            var result = CreateDefaultInstanceLogEntry();

            var request = new HttpRequestMessage(HttpMethod.Post, "http://someuri/api/path");
            request.Headers.Add("User-Agent", "my custom user agent");
            var response = new HttpResponseMessage();
            request.Properties[ApplicationInsightsScopeKeys.FunctionsHttpResponse] = response;

            MockIpAddress(request, "1.2.3.4");

            ILogger logger = CreateLogger(LogCategories.Results);
            var scopeProps = CreateScopeDictionary(_invocationId, _functionShortName);
            scopeProps[ApplicationInsightsScopeKeys.HttpRequest] = request;

            using (logger.BeginScope(scopeProps))
            {
                logger.LogFunctionResult(_functionShortName, result, TimeSpan.FromMilliseconds(durationMs));
            }

            RequestTelemetry telemetry = _channel.Telemetries.Single() as RequestTelemetry;

            Assert.Equal(_invocationId.ToString(), telemetry.Id);
            Assert.Equal(_invocationId.ToString(), telemetry.Context.Operation.Id);
            Assert.Equal(_functionShortName, telemetry.Name);
            Assert.Equal(_functionShortName, telemetry.Context.Operation.Name);
            Assert.Equal("1.2.3.4", telemetry.Context.Location.Ip);
            Assert.Equal("POST", telemetry.Properties[LoggingKeys.HttpMethod]);
            Assert.Equal(new Uri("http://someuri/api/path"), telemetry.Url);
            Assert.Equal("my custom user agent", telemetry.Context.User.UserAgent);
            Assert.Equal("200", telemetry.ResponseCode);
            Assert.Equal(LogCategories.Results, telemetry.Properties[LoggingKeys.CategoryName]);
            Assert.Equal(LogLevel.Information.ToString(), telemetry.Properties[LoggingKeys.LogLevel]);
            // TODO: Beef up validation to include properties      
        }

        [Fact]
        public void LogFunctionResult_HttpRequest_WithException_SendsCorrectTelemetry()
        {
            // If the scope has an HttpRequestMessage, we'll use the proper values
            // for the RequestTelemetry
            DateTime now = DateTime.UtcNow;
            var result = CreateDefaultInstanceLogEntry();

            var request = new HttpRequestMessage(HttpMethod.Post, "http://someuri/api/path");
            request.Headers.Add("User-Agent", "my custom user agent");

            // In the case of an exception being thrown, no response is attached

            MockIpAddress(request, "1.2.3.4");

            ILogger logger = CreateLogger(LogCategories.Results);
            var scopeProps = CreateScopeDictionary(_invocationId, _functionShortName);
            scopeProps[ApplicationInsightsScopeKeys.HttpRequest] = request;

            Exception fex = new Exception("Boom");
            using (logger.BeginScope(scopeProps))
            {
                logger.LogFunctionResult(_functionShortName, result, TimeSpan.FromMilliseconds(durationMs), fex);
            }

            // one Exception, one Request
            Assert.Equal(2, _channel.Telemetries.Count);

            RequestTelemetry requestTelemetry = _channel.Telemetries.Where(t => t is RequestTelemetry).Single() as RequestTelemetry;
            ExceptionTelemetry exceptionTelemetry = _channel.Telemetries.Where(t => t is ExceptionTelemetry).Single() as ExceptionTelemetry;

            Assert.Equal(_invocationId.ToString(), requestTelemetry.Id);
            Assert.Equal(_invocationId.ToString(), requestTelemetry.Context.Operation.Id);
            Assert.Equal(_functionShortName, requestTelemetry.Name);
            Assert.Equal(_functionShortName, requestTelemetry.Context.Operation.Name);
            Assert.Equal("1.2.3.4", requestTelemetry.Context.Location.Ip);
            Assert.Equal("POST", requestTelemetry.Properties[LoggingKeys.HttpMethod]);
            Assert.Equal(new Uri("http://someuri/api/path"), requestTelemetry.Url);
            Assert.Equal("my custom user agent", requestTelemetry.Context.User.UserAgent);
            Assert.Equal("500", requestTelemetry.ResponseCode);
            Assert.Equal(LogCategories.Results, requestTelemetry.Properties[LoggingKeys.CategoryName]);
            Assert.Equal(LogLevel.Error.ToString(), requestTelemetry.Properties[LoggingKeys.LogLevel]);
            // TODO: Beef up validation to include properties      

            // Exception needs to have associated id
            Assert.Equal(_invocationId.ToString(), exceptionTelemetry.Context.Operation.Id);
            Assert.Equal(_functionShortName, exceptionTelemetry.Context.Operation.Name);
            Assert.Same(fex, exceptionTelemetry.Exception);
            Assert.Equal(LogCategories.Results, exceptionTelemetry.Properties[LoggingKeys.CategoryName]);
            Assert.Equal(LogLevel.Error.ToString(), exceptionTelemetry.Properties[LoggingKeys.LogLevel]);
            // TODO: Beef up validation to include properties
        }

        [Fact]
        public void Log_NoProperties_CreatesTraceAndCorrelates()
        {
            Guid scopeGuid = Guid.NewGuid();

            ILogger logger = CreateLogger(LogCategories.Function);
            using (logger.BeginFunctionScope(CreateFunctionInstance(scopeGuid)))
            {
                logger.LogInformation("Information");
                logger.LogCritical("Critical");
                logger.LogDebug("Debug");
                logger.LogError("Error");
                logger.LogTrace("Trace");
                logger.LogWarning("Warning");
            }

            Assert.Equal(6, _channel.Telemetries.Count);
            Assert.Equal(6, _channel.Telemetries.OfType<TraceTelemetry>().Count());
            foreach (var telemetry in _channel.Telemetries.Cast<TraceTelemetry>())
            {
                LogLevel expectedLogLevel;
                Enum.TryParse(telemetry.Message, out expectedLogLevel);
                Assert.Equal(expectedLogLevel.ToString(), telemetry.Properties[LoggingKeys.LogLevel]);

                SeverityLevel expectedSeverityLevel;
                if (telemetry.Message == "Trace" || telemetry.Message == "Debug")
                {
                    expectedSeverityLevel = SeverityLevel.Verbose;
                }
                else
                {
                    Assert.True(Enum.TryParse(telemetry.Message, out expectedSeverityLevel));
                }
                Assert.Equal(expectedSeverityLevel, telemetry.SeverityLevel);

                Assert.Equal(LogCategories.Function, telemetry.Properties[LoggingKeys.CategoryName]);
                Assert.Equal(telemetry.Message, telemetry.Properties[LoggingKeys.CustomPropertyPrefix + LoggingKeys.OriginalFormat]);
                Assert.Equal(scopeGuid.ToString(), telemetry.Context.Operation.Id);
                Assert.Equal(_functionShortName, telemetry.Context.Operation.Name);
            }
        }

        [Fact]
        public void Log_WithProperties_IncludesProps()
        {
            ILogger logger = CreateLogger(LogCategories.Function);
            logger.LogInformation("Using {some} custom {properties}. {Test}.", "1", 2, "3");

            var telemetry = _channel.Telemetries.Single() as TraceTelemetry;

            Assert.Equal(SeverityLevel.Information, telemetry.SeverityLevel);

            Assert.Equal(LogCategories.Function, telemetry.Properties[LoggingKeys.CategoryName]);
            Assert.Equal(LogLevel.Information.ToString(), telemetry.Properties[LoggingKeys.LogLevel]);
            Assert.Equal("Using {some} custom {properties}. {Test}.",
                telemetry.Properties[LoggingKeys.CustomPropertyPrefix + LoggingKeys.OriginalFormat]);
            Assert.Equal("Using 1 custom 2. 3.", telemetry.Message);
            Assert.Equal("1", telemetry.Properties[LoggingKeys.CustomPropertyPrefix + "some"]);
            Assert.Equal("2", telemetry.Properties[LoggingKeys.CustomPropertyPrefix + "properties"]);
            Assert.Equal("3", telemetry.Properties[LoggingKeys.CustomPropertyPrefix + "Test"]);
        }

        [Fact]
        public void Log_WithException_CreatesExceptionAndCorrelates()
        {
            var ex = new InvalidOperationException("Failure");
            Guid scopeGuid = Guid.NewGuid();
            ILogger logger = CreateLogger(LogCategories.Function);

            using (logger.BeginFunctionScope(CreateFunctionInstance(scopeGuid)))
            {
                logger.LogError(0, ex, "Error with customer: {customer}.", "John Doe");
            }

            var telemetry = _channel.Telemetries.Single() as ExceptionTelemetry;

            Assert.Equal(SeverityLevel.Error, telemetry.SeverityLevel);

            Assert.Equal(LogCategories.Function, telemetry.Properties[LoggingKeys.CategoryName]);
            Assert.Equal(LogLevel.Error.ToString(), telemetry.Properties[LoggingKeys.LogLevel]);
            Assert.Equal("Error with customer: {customer}.",
                telemetry.Properties[LoggingKeys.CustomPropertyPrefix + LoggingKeys.OriginalFormat]);
            Assert.Equal("Error with customer: John Doe.", telemetry.Message);
            Assert.Equal("John Doe", telemetry.Properties[LoggingKeys.CustomPropertyPrefix + "customer"]);
            Assert.Same(ex, telemetry.Exception);
            Assert.Equal(scopeGuid.ToString(), telemetry.Context.Operation.Id);
            Assert.Equal(_functionShortName, telemetry.Context.Operation.Name);
        }

        [Theory]
        [InlineData("1.2.3.4:5")]
        [InlineData("1.2.3.4")]
        public void GetIpAddress_ChecksHeaderFirst(string headerIp)
        {
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(ApplicationInsightsScopeKeys.ForwardedForHeaderName, headerIp);
            MockIpAddress(request, "5.6.7.8");

            string ip = ApplicationInsightsLogger.GetIpAddress(request);

            Assert.Equal("1.2.3.4", ip);
        }

        [Fact]
        public void GetIpAddress_ChecksContextSecond()
        {
            HttpRequestMessage request = new HttpRequestMessage();
            MockIpAddress(request, "5.6.7.8");

            string ip = ApplicationInsightsLogger.GetIpAddress(request);

            Assert.Equal("5.6.7.8", ip);
        }

        [Fact]
        public async Task BeginScope()
        {
            List<Task> tasks = new List<Task>();
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(Level1(Guid.NewGuid()));
            }

            await Task.WhenAll(tasks);
        }

        private async Task Level1(Guid asyncLocalSetting)
        {
            // Push and pop values onto the dictionary at various levels. Make sure they
            // maintain their AsyncLocal state
            var level1 = new Dictionary<string, object>
            {
                ["AsyncLocal"] = asyncLocalSetting,
                ["1"] = 1
            };

            ILogger logger = CreateLogger(LogCategories.Function);
            using (logger.BeginScope(level1))
            {
                ValidateScope(level1);

                await Level2(asyncLocalSetting);

                ValidateScope(level1);
            }
        }

        private async Task Level2(Guid asyncLocalSetting)
        {
            await Task.Delay(1);

            var level2 = new Dictionary<string, object>
            {
                ["2"] = 2
            };

            var expectedLevel2 = new Dictionary<string, object>
            {
                ["1"] = 1,
                ["2"] = 2,
                ["AsyncLocal"] = asyncLocalSetting
            };

            ILogger logger2 = CreateLogger(LogCategories.Function);
            using (logger2.BeginScope(level2))
            {
                ValidateScope(expectedLevel2);

                await Level3(asyncLocalSetting);

                ValidateScope(expectedLevel2);
            }
        }

        private async Task Level3(Guid asyncLocalSetting)
        {
            await Task.Delay(1);

            // also overwrite value 1, we expect this to win here
            var level3 = new Dictionary<string, object>
            {
                ["1"] = 11,
                ["3"] = 3
            };

            var expectedLevel3 = new Dictionary<string, object>
            {
                ["1"] = 11,
                ["2"] = 2,
                ["3"] = 3,
                ["AsyncLocal"] = asyncLocalSetting
            };

            ILogger logger3 = CreateLogger(LogCategories.Function);
            using (logger3.BeginScope(level3))
            {
                ValidateScope(expectedLevel3);
            }
        }

        private static void MockIpAddress(HttpRequestMessage request, string ipAddress)
        {
            Mock<HttpContextBase> mockContext = new Mock<HttpContextBase>(MockBehavior.Strict);
            Mock<HttpRequestBase> mockRequest = new Mock<HttpRequestBase>(MockBehavior.Strict);
            mockRequest.Setup(r => r.UserHostAddress).Returns(ipAddress);
            mockContext.Setup(c => c.Request).Returns(mockRequest.Object);
            request.Properties[ApplicationInsightsScopeKeys.HttpContext] = mockContext.Object;
        }

        private ILogger CreateLogger(string category)
        {
            return new ApplicationInsightsLogger(_client, category);
        }

        private static void ValidateScope(IDictionary<string, object> expected)
        {
            var scopeDict = DictionaryLoggerScope.GetMergedStateDictionary();
            Assert.Equal(expected.Count, scopeDict.Count);
            foreach (var entry in expected)
            {
                Assert.Equal(entry.Value, scopeDict[entry.Key]);
            }
        }

        private IFunctionInstance CreateFunctionInstance(Guid id)
        {
            var descriptor = new FunctionDescriptor
            {
                Method = GetType().GetMethod(nameof(TestFunction), BindingFlags.NonPublic | BindingFlags.Static)
            };

            return new FunctionInstance(id, null, new ExecutionReason(), null, null, descriptor);
        }

        private static IDictionary<string, object> CreateScopeDictionary(Guid invocationId, string functionName)
        {
            return new Dictionary<string, object>
            {
                [ScopeKeys.FunctionInvocationId] = invocationId,
                [ScopeKeys.FunctionName] = functionName
            };
        }

        private static void TestFunction()
        {
            // used for a FunctionDescriptor
        }

        private FunctionInstanceLogEntry CreateDefaultInstanceLogEntry()
        {
            return new FunctionInstanceLogEntry
            {
                FunctionName = _functionFullName,
                FunctionInstanceId = _invocationId,
                StartTime = _startTime,
                EndTime = _endTime,
                LogOutput = "a bunch of output that we will not forward", // not used here -- this is all Traced
                TriggerReason = _triggerReason,
                ParentId = Guid.NewGuid(), // we do not track this
                ErrorDetails = null, // we do not use this -- we pass the exception in separately
                Arguments = _arguments
            };
        }

    }
}
