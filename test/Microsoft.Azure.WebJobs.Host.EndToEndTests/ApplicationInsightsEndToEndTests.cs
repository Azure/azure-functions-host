// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class ApplicationInsightsEndToEndTests
    {
        private readonly TestTelemetryChannel _channel = new TestTelemetryChannel();
        private const string _mockApplicationInsightsKey = "some_key";

        [Fact]
        public async Task ApplicationInsights_SuccessfulFunction()
        {
            string testName = nameof(TestApplicationInsightsInformation);
            LogCategoryFilter filter = new LogCategoryFilter();
            filter.DefaultLevel = LogLevel.Information;

            var loggerFactory = new LoggerFactory()
                .AddApplicationInsights(
                    new TestTelemetryClientFactory(filter.Filter, _channel));

            JobHostConfiguration config = new JobHostConfiguration
            {
                LoggerFactory = loggerFactory,
                TypeLocator = new FakeTypeLocator(GetType()),                
            };
            config.Aggregator.IsEnabled = false;
            config.AddService<IWebJobsExceptionHandler>(new TestExceptionHandler());

            using (JobHost host = new JobHost(config))
            {
                await host.StartAsync();
                var methodInfo = GetType().GetMethod(testName, BindingFlags.Public | BindingFlags.Static);
                await host.CallAsync(methodInfo, new { input = "function input" });
                await host.StopAsync();
            }

            Assert.Equal(6, _channel.Telemetries.Count);

            // Validate the traces. Order by message string as the requests may come in
            // slightly out-of-order or on different threads
            TraceTelemetry[] telemetries = _channel.Telemetries
                .OfType<TraceTelemetry>()
                .OrderBy(t => t.Message)
                .ToArray();

            ValidateTrace(telemetries[0], "Found the following functions:\r\n", LogCategories.Startup);
            ValidateTrace(telemetries[1], "Job host started", LogCategories.Startup);
            ValidateTrace(telemetries[2], "Job host stopped", LogCategories.Startup);
            ValidateTrace(telemetries[3], "Logger", LogCategories.Function, testName);
            ValidateTrace(telemetries[4], "Trace", LogCategories.Function, testName);

            // Finally, validate the request
            RequestTelemetry request = _channel.Telemetries
                .OfType<RequestTelemetry>()
                .Single();
            ValidateRequest(request, testName, true);
        }

        [Fact]
        public async Task ApplicationInsights_FailedFunction()
        {
            string testName = nameof(TestApplicationInsightsFailure);
            LogCategoryFilter filter = new LogCategoryFilter();
            filter.DefaultLevel = LogLevel.Information;

            var loggerFactory = new LoggerFactory()
                .AddApplicationInsights(
                    new TestTelemetryClientFactory(filter.Filter, _channel));

            JobHostConfiguration config = new JobHostConfiguration
            {
                LoggerFactory = loggerFactory,
                TypeLocator = new FakeTypeLocator(GetType()),
            };
            config.Aggregator.IsEnabled = false;
            config.AddService<IWebJobsExceptionHandler>(new TestExceptionHandler());

            using (JobHost host = new JobHost(config))
            {
                await host.StartAsync();
                var methodInfo = GetType().GetMethod(testName, BindingFlags.Public | BindingFlags.Static);
                await Assert.ThrowsAsync<FunctionInvocationException>(() => host.CallAsync(methodInfo, new { input = "function input" }));
                await host.StopAsync();
            }

            Assert.Equal(7, _channel.Telemetries.Count);

            // Validate the traces. Order by message string as the requests may come in
            // slightly out-of-order or on different threads
            TraceTelemetry[] telemetries = _channel.Telemetries
             .OfType<TraceTelemetry>()
             .OrderBy(t => t.Message)
             .ToArray();

            ValidateTrace(telemetries[0], "Found the following functions:\r\n", LogCategories.Startup);
            ValidateTrace(telemetries[1], "Job host started", LogCategories.Startup);
            ValidateTrace(telemetries[2], "Job host stopped", LogCategories.Startup);
            ValidateTrace(telemetries[3], "Logger", LogCategories.Function, testName);
            ValidateTrace(telemetries[4], "Trace", LogCategories.Function, testName);

            // Validate the exception
            ExceptionTelemetry exception = _channel.Telemetries
                .OfType<ExceptionTelemetry>()
                .Single();
            ValidateException(exception, testName);

            // Finally, validate the request
            RequestTelemetry request = _channel.Telemetries
                .OfType<RequestTelemetry>()
                .Single();
            ValidateRequest(request, testName, false);
        }

        // Test Functions
        [NoAutomaticTrigger]
        public static void TestApplicationInsightsInformation(string input, TraceWriter trace, ILogger logger)
        {
            trace.Info("Trace");
            logger.LogInformation("Logger");
        }

        [NoAutomaticTrigger]
        public static void TestApplicationInsightsFailure(string input, TraceWriter trace, ILogger logger)
        {
            trace.Info("Trace");
            logger.LogInformation("Logger");

            throw new Exception("Boom!");
        }

        private static void ValidateTrace(TraceTelemetry telemetry, string expectedMessageStartsWith,
            string expectedCategory, string expectedOperationName = null)
        {
            Assert.StartsWith(expectedMessageStartsWith, telemetry.Message);
            Assert.Equal(SeverityLevel.Information, telemetry.SeverityLevel);

            Assert.Equal(expectedCategory, telemetry.Properties["Category"]);

            if (expectedCategory == LogCategories.Function || expectedCategory == LogCategories.Executor)
            {
                // These should have associated operation information
                Assert.Equal(expectedOperationName, telemetry.Context.Operation.Name);
                Assert.NotNull(telemetry.Context.Operation.Id);
            }
            else
            {
                Assert.Null(telemetry.Context.Operation.Name);
                Assert.Null(telemetry.Context.Operation.Id);
            }

            ValidateSdkVersion(telemetry);
        }


        private static void ValidateException(ExceptionTelemetry telemetryItem, string expectedOperationName)
        {
            Assert.Equal("Host.Results", telemetryItem.Properties["Category"]);
            Assert.Equal(expectedOperationName, telemetryItem.Context.Operation.Name);
            Assert.NotNull(telemetryItem.Context.Operation.Id);

            // Check that the Function details show up as 'prop__'. We may change this in the future as
            // it may not be exceptionally useful.
            Assert.Equal(expectedOperationName, telemetryItem.Properties[$"{LogConstants.CustomPropertyPrefix}{LogConstants.NameKey}"]);
            Assert.Equal("This function was programmatically called via the host APIs.", telemetryItem.Properties[$"{LogConstants.CustomPropertyPrefix}{LogConstants.TriggerReasonKey}"]);

            // TODO: Parameter logging shouldn't have prop__ prefixes. Need to revisit.
            Assert.Equal("function input", telemetryItem.Properties[$"{LogConstants.CustomPropertyPrefix}{LogConstants.ParameterPrefix}input"]);
                        
            Assert.IsType<FunctionInvocationException>(telemetryItem.Exception);
            Assert.IsType<Exception>(telemetryItem.Exception.InnerException);

            ValidateSdkVersion(telemetryItem);
        }

        private static void ValidateRequest(RequestTelemetry telemetry, string operationName, bool success)
        {
            Assert.NotNull(telemetry.Context.Operation.Id);
            Assert.Equal(operationName, telemetry.Context.Operation.Name);
            Assert.NotNull(telemetry.Duration);
            Assert.Equal(success, telemetry.Success);

            Assert.NotNull(telemetry.Properties[$"{LogConstants.ParameterPrefix}input"]);
            Assert.Equal($"ApplicationInsightsEndToEndTests.{operationName}", telemetry.Properties[LogConstants.FullNameKey].ToString());
            Assert.Equal("This function was programmatically called via the host APIs.", telemetry.Properties[LogConstants.TriggerReasonKey].ToString());

            ValidateSdkVersion(telemetry);
        }

        private static void ValidateSdkVersion(ITelemetry telemetry)
        {
            PropertyInfo propInfo = typeof(TelemetryContext).GetProperty("Tags", BindingFlags.NonPublic | BindingFlags.Instance);
            IDictionary<string, string> tags = propInfo.GetValue(telemetry.Context) as IDictionary<string, string>;

            Assert.StartsWith("webjobs: ", tags["ai.internal.sdkVersion"]);
        }

        private class TestTelemetryClientFactory : DefaultTelemetryClientFactory
        {
            private TestTelemetryChannel _channel;

            public TestTelemetryClientFactory(Func<string, LogLevel, bool> filter, TestTelemetryChannel channel)                
                : base(_mockApplicationInsightsKey, filter)
            {
                _channel = channel;
            }            

            protected override ITelemetryChannel CreateTelemetryChannel()
            {
                return _channel;
            }
        }

        private class TestTelemetryChannel : ITelemetryChannel
        {
            public ConcurrentBag<ITelemetry> Telemetries = new ConcurrentBag<ITelemetry>();

            public bool? DeveloperMode { get; set; }

            public string EndpointAddress { get; set; }

            public void Dispose()
            {
            }

            public void Flush()
            {
            }

            public void Send(ITelemetry item)
            {
                Telemetries.Add(item);
            }
        }
    }
}
