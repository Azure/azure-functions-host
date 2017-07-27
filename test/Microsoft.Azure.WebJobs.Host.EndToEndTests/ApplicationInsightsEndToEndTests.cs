using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.QuickPulse;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class ApplicationInsightsEndToEndTests
    {
        private const string _mockApplicationInsightsUrl = "http://localhost:4005/v2/track/";
        private const string _mockQuickPulseUrl = "http://localhost:4005/QuickPulseService.svc/";
        private const string _mockApplicationInsightsKey = "some_key";

        [Fact]
        public async Task ApplicationInsights_SuccessfulFunction()
        {
            string testName = nameof(TestApplicationInsightsInformation);
            LogCategoryFilter filter = new LogCategoryFilter();
            filter.DefaultLevel = LogLevel.Information;

            var loggerFactory = new LoggerFactory()
                .AddApplicationInsights(
                    new TestTelemetryClientFactory(_mockApplicationInsightsKey, new SamplingPercentageEstimatorSettings(), filter.Filter));

            JobHostConfiguration config = new JobHostConfiguration
            {
                LoggerFactory = loggerFactory,
                TypeLocator = new FakeTypeLocator(GetType()),
            };
            config.Aggregator.IsEnabled = false;

            using (var listener = new ApplicationInsightsTestListener())
            {
                listener.StartListening();

                using (JobHost host = new JobHost(config))
                {
                    await host.StartAsync();
                    var methodInfo = GetType().GetMethod(testName, BindingFlags.Public | BindingFlags.Static);
                    await host.CallAsync(methodInfo, new { input = "function input" });
                    await host.StopAsync();
                }

                // wait for everything to flush
                await Task.Delay(2000);

                Assert.Equal(6, listener.TelemetryItems.Count);

                // Validate the traces. Order by message string as the requests may come in
                // slightly out-of-order or on different threads
                TelemetryPayload[] telemetries = listener.TelemetryItems
                    .Where(t => t.Data.BaseType == "MessageData")
                    .OrderBy(t => t.Data.BaseData.Message)
                    .ToArray();

                ValidateTrace(telemetries[0], "Found the following functions:\r\n", LogCategories.Startup);
                ValidateTrace(telemetries[1], "Job host started", LogCategories.Startup);
                ValidateTrace(telemetries[2], "Job host stopped", LogCategories.Startup);
                ValidateTrace(telemetries[3], "Logger", LogCategories.Function, testName);
                ValidateTrace(telemetries[4], "Trace", LogCategories.Function, testName);

                // Finally, validate the request
                TelemetryPayload request = listener.TelemetryItems
                    .Where(t => t.Data.BaseType == "RequestData")
                    .Single();
                ValidateRequest(request, testName, true);
            }
        }

        [Fact]
        public async Task ApplicationInsights_FailedFunction()
        {
            string testName = nameof(TestApplicationInsightsFailure);
            LogCategoryFilter filter = new LogCategoryFilter();
            filter.DefaultLevel = LogLevel.Information;

            var loggerFactory = new LoggerFactory()
                .AddApplicationInsights(
                    new TestTelemetryClientFactory(_mockApplicationInsightsKey, new SamplingPercentageEstimatorSettings(), filter.Filter));

            JobHostConfiguration config = new JobHostConfiguration
            {
                LoggerFactory = loggerFactory,
                TypeLocator = new FakeTypeLocator(GetType()),
            };
            config.Aggregator.IsEnabled = false;

            using (var listener = new ApplicationInsightsTestListener())
            {
                listener.StartListening();

                using (JobHost host = new JobHost(config))
                {
                    await host.StartAsync();
                    var methodInfo = GetType().GetMethod(testName, BindingFlags.Public | BindingFlags.Static);
                    await Assert.ThrowsAsync<FunctionInvocationException>(() => host.CallAsync(methodInfo, new { input = "function input" }));
                    await host.StopAsync();
                }

                // wait for everything to flush
                await Task.Delay(2000);

                Assert.Equal(7, listener.TelemetryItems.Count);

                // Validate the traces. Order by message string as the requests may come in
                // slightly out-of-order or on different threads
                TelemetryPayload[] telemetries = listener.TelemetryItems
                    .Where(t => t.Data.BaseType == "MessageData")
                    .OrderBy(t => t.Data.BaseData.Message)
                    .ToArray();

                ValidateTrace(telemetries[0], "Found the following functions:\r\n", LogCategories.Startup);
                ValidateTrace(telemetries[1], "Job host started", LogCategories.Startup);
                ValidateTrace(telemetries[2], "Job host stopped", LogCategories.Startup);
                ValidateTrace(telemetries[3], "Logger", LogCategories.Function, testName);
                ValidateTrace(telemetries[4], "Trace", LogCategories.Function, testName);

                // Validate the exception
                TelemetryPayload exception = listener.TelemetryItems
                    .Where(t => t.Data.BaseType == "ExceptionData")
                    .Single();
                ValidateException(exception, testName);

                // Finally, validate the request
                TelemetryPayload request = listener.TelemetryItems
                    .Where(t => t.Data.BaseType == "RequestData")
                    .Single();
                ValidateRequest(request, testName, false);
            }
        }

        [Theory]
        [InlineData(LogLevel.None, 0)]
        [InlineData(LogLevel.Information, 18)]
        [InlineData(LogLevel.Warning, 10)]
        public async Task QuickPulse_Works_EvenIfFiltered(LogLevel defaultLevel, int expectedTelemetryItems)
        {
            LogCategoryFilter filter = new LogCategoryFilter();
            filter.DefaultLevel = defaultLevel;

            var loggerFactory = new LoggerFactory()
                .AddApplicationInsights(
                    new TestTelemetryClientFactory(_mockApplicationInsightsKey, new SamplingPercentageEstimatorSettings(), filter.Filter));

            JobHostConfiguration config = new JobHostConfiguration
            {
                LoggerFactory = loggerFactory,
                TypeLocator = new FakeTypeLocator(GetType()),
            };
            config.Aggregator.IsEnabled = false;

            using (var listener = new ApplicationInsightsTestListener())
            {
                listener.StartListening();

                int requests = 5;
                using (JobHost host = new JobHost(config))
                {
                    await host.StartAsync();

                    var methodInfo = GetType().GetMethod(nameof(TestApplicationInsightsWarning), BindingFlags.Public | BindingFlags.Static);

                    for (int i = 0; i < requests; i++)
                    {
                        await host.CallAsync(methodInfo);
                    }

                    await host.StopAsync();
                }

                // wait for everything to flush
                await Task.Delay(2000);

                // Sum up all req/sec calls that we've received.
                var reqPerSec = listener
                    .QuickPulseItems.Select(p => p.Metrics.Where(q => q.Name == @"\ApplicationInsights\Requests/Sec").Single());
                double sum = reqPerSec.Sum(p => p.Value);

                // All requests will go to QuickPulse.
                // The calculated RPS may off, so give some wiggle room. The important thing is that it's generating 
                // RequestTelemetry and not being filtered.
                double max = requests + 3;
                double min = requests - 1;
                Assert.True(sum > min && sum < max, $"Expected sum to be greater than {min} and less than {max}. DefaultLevel: {defaultLevel}. Actual: {sum}");

                // These will be filtered based on the default filter.
                Assert.Equal(expectedTelemetryItems, listener.TelemetryItems.Count());
            }
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

        [NoAutomaticTrigger]
        public static void TestApplicationInsightsWarning(TraceWriter trace, ILogger logger)
        {
            trace.Warning("Trace");
            logger.LogWarning("Logger");
        }

        private class ApplicationInsightsTestListener : IDisposable
        {

            private readonly HttpListener _applicationInsightsListener = new HttpListener();
            private Thread _listenerThread;

            public List<TelemetryPayload> TelemetryItems { get; } = new List<TelemetryPayload>();

            public List<QuickPulsePayload> QuickPulseItems { get; } = new List<QuickPulsePayload>();

            public void StartListening()
            {
                _applicationInsightsListener.Prefixes.Add(_mockApplicationInsightsUrl);
                _applicationInsightsListener.Prefixes.Add(_mockQuickPulseUrl);
                _applicationInsightsListener.Start();
                Listen();
            }

            private void Listen()
            {
                // process a request, then continue to wait for the next
                _listenerThread = new Thread(() =>
                {
                    while (_applicationInsightsListener.IsListening)
                    {
                        try
                        {
                            HttpListenerContext context = _applicationInsightsListener.GetContext();
                            ProcessRequest(context);
                        }
                        catch (HttpListenerException)
                        {
                            // This happens when stopping the listener.
                        }
                    }
                });

                _listenerThread.Start();
            }

            private void ProcessRequest(HttpListenerContext context)
            {
                var request = context.Request;
                var response = context.Response;

                try
                {
                    if (request.Url.OriginalString.StartsWith(_mockQuickPulseUrl))
                    {
                        HandleQuickPulseRequest(request, response);
                    }
                    else
                    {
                        HandleTelemetryRequest(request);
                    }
                }
                finally
                {
                    response.Close();
                }
            }

            private void HandleQuickPulseRequest(HttpListenerRequest request, HttpListenerResponse response)
            {
                string result = GetRequestContent(request);
                response.AddHeader("x-ms-qps-subscribed", true.ToString());

                if (request.Url.LocalPath == "/QuickPulseService.svc/post")
                {
                    QuickPulsePayload[] quickPulse = JsonConvert.DeserializeObject<QuickPulsePayload[]>(result);
                    QuickPulseItems.AddRange(quickPulse);
                }
            }

            private void HandleTelemetryRequest(HttpListenerRequest request)
            {
                string result = GetRequestContent(request);

                if (!string.IsNullOrWhiteSpace(request.Headers["Content-Encoding"]) &&
                       string.Equals("gzip", request.Headers["Content-Encoding"],
                           StringComparison.InvariantCultureIgnoreCase))
                {
                    result = Decompress(result);
                }

                TelemetryPayload telemetry = JsonConvert.DeserializeObject<TelemetryPayload>(result);
                TelemetryItems.Add(telemetry);
            }

            private static string GetRequestContent(HttpListenerRequest request)
            {
                string result = null;
                if (request.HasEntityBody)
                {
                    using (var requestInputStream = request.InputStream)
                    {
                        var encoding = request.ContentEncoding;
                        using (var reader = new StreamReader(requestInputStream, encoding))
                        {
                            result = reader.ReadToEnd();
                        }
                    }
                }
                return result;
            }

            private static string Decompress(string content)
            {
                var zippedData = Encoding.Default.GetBytes(content);
                using (var ms = new MemoryStream(zippedData))
                {
                    using (var compressedzipStream = new GZipStream(ms, CompressionMode.Decompress))
                    {
                        var outputStream = new MemoryStream();
                        var block = new byte[1024];
                        while (true)
                        {
                            int bytesRead = compressedzipStream.Read(block, 0, block.Length);
                            if (bytesRead <= 0)
                            {
                                break;
                            }

                            outputStream.Write(block, 0, bytesRead);
                        }
                        compressedzipStream.Close();
                        return Encoding.UTF8.GetString(outputStream.ToArray());
                    }
                }
            }

            public void Dispose()
            {
                _applicationInsightsListener.Stop();
                _listenerThread.Join();
            }
        }

        private class TestTelemetryClientFactory : DefaultTelemetryClientFactory
        {
            public TestTelemetryClientFactory(string instrumentationKey, SamplingPercentageEstimatorSettings samplingSettings, Func<string, LogLevel, bool> filter)
                : base(instrumentationKey, samplingSettings, filter)
            {
            }

            protected override QuickPulseTelemetryModule CreateQuickPulseTelemetryModule()
            {
                QuickPulseTelemetryModule module = base.CreateQuickPulseTelemetryModule();
                module.QuickPulseServiceEndpoint = _mockQuickPulseUrl;
                return module;
            }

            protected override ITelemetryChannel CreateTelemetryChannel()
            {
                ITelemetryChannel channel = base.CreateTelemetryChannel();
                channel.EndpointAddress = _mockApplicationInsightsUrl;

                // DeveloperMode prevents buffering so items are sent immediately.
                channel.DeveloperMode = true;
                ((ServerTelemetryChannel)channel).MaxTelemetryBufferDelay = TimeSpan.FromSeconds(1);

                return channel;
            }
        }

        private static void ValidateTrace(TelemetryPayload telemetryItem, string expectedMessageStartsWith,
            string expectedCategory, string expectedOperationName = null)
        {
            Assert.Equal("MessageData", telemetryItem.Data.BaseType);

            Assert.StartsWith(expectedMessageStartsWith, telemetryItem.Data.BaseData.Message);
            Assert.Equal("Information", telemetryItem.Data.BaseData.SeverityLevel);

            Assert.Equal(expectedCategory, telemetryItem.Data.BaseData.Properties["Category"]);

            if (expectedCategory == LogCategories.Function || expectedCategory == LogCategories.Executor)
            {
                // These should have associated operation information
                Assert.Equal(expectedOperationName, telemetryItem.Tags["ai.operation.name"]);
                Assert.NotNull(telemetryItem.Tags["ai.operation.id"]);
            }
            else
            {
                Assert.DoesNotContain("ai.operation.name", telemetryItem.Tags.Keys);
                Assert.DoesNotContain("ai.operation.id", telemetryItem.Tags.Keys);
            }

            ValidateSdkVersion(telemetryItem);
        }

        private static void ValidateException(TelemetryPayload telemetryItem, string expectedOperationName)
        {
            Assert.Equal("ExceptionData", telemetryItem.Data.BaseType);

            Assert.Null(telemetryItem.Data.BaseData.Name);
            Assert.Equal("Host.Results", telemetryItem.Data.BaseData.Properties["Category"]);
            Assert.Equal(expectedOperationName, telemetryItem.Tags["ai.operation.name"]);
            Assert.NotNull(telemetryItem.Tags["ai.operation.id"]);

            // Check that the Function details show up as 'prop__'. We may change this in the future as
            // it may not be exceptionally useful.
            Assert.Equal(expectedOperationName, telemetryItem.Data.BaseData.Properties[$"{LoggingKeys.CustomPropertyPrefix}{LoggingKeys.Name}"]);
            Assert.Equal("This function was programmatically called via the host APIs.", telemetryItem.Data.BaseData.Properties[$"{LoggingKeys.CustomPropertyPrefix}{LoggingKeys.TriggerReason}"]);

            // TODO: Parameter logging shouldn't have prop__ prefixes. Need to revisit.
            Assert.Equal("function input", telemetryItem.Data.BaseData.Properties[$"{LoggingKeys.CustomPropertyPrefix}{LoggingKeys.ParameterPrefix}input"]);

            Assert.Equal(2, telemetryItem.Data.BaseData.Exceptions.Length);

            TelemetryException first = telemetryItem.Data.BaseData.Exceptions[0];
            Assert.Equal("Microsoft.Azure.WebJobs.Host.FunctionInvocationException", first.TypeName);
            Assert.Equal("n/a", first.Message);
            Assert.True(first.HasFullStack);

            TelemetryException second = telemetryItem.Data.BaseData.Exceptions[1];
            Assert.Equal("System.Exception", second.TypeName);
            Assert.Equal("Boom!", second.Message);
            Assert.True(second.HasFullStack);

            ValidateSdkVersion(telemetryItem);
        }

        private static void ValidateRequest(TelemetryPayload telemetryItem, string operationName, bool success)
        {
            Assert.Equal("RequestData", telemetryItem.Data.BaseType);

            Assert.NotNull(telemetryItem.Data.BaseData.Id);
            Assert.Equal(operationName, telemetryItem.Data.BaseData.Name);
            Assert.NotNull(telemetryItem.Data.BaseData.Duration);
            Assert.Equal(success, telemetryItem.Data.BaseData.Success);

            Assert.NotNull(telemetryItem.Data.BaseData.Properties[$"{LoggingKeys.ParameterPrefix}input"]);
            Assert.Equal($"ApplicationInsightsEndToEndTests.{operationName}", telemetryItem.Data.BaseData.Properties[LoggingKeys.FullName].ToString());
            Assert.Equal("This function was programmatically called via the host APIs.", telemetryItem.Data.BaseData.Properties[LoggingKeys.TriggerReason].ToString());

            ValidateSdkVersion(telemetryItem);
        }

        private static void ValidateSdkVersion(TelemetryPayload telemetryItem)
        {
            Assert.StartsWith("webjobs: ", telemetryItem.Tags["ai.internal.sdkVersion"]);
        }

        private class TelemetryPayload
        {
            public string Name { get; set; }

            public DateTime Time { get; set; }

            public string IKey { get; set; }

            public IDictionary<string, string> Tags { get; } = new Dictionary<string, string>();

            public TelemetryData Data { get; } = new TelemetryData();

            public override string ToString()
            {
                return Data.BaseData.Message;
            }
        }

        private class TelemetryData
        {
            public string BaseType { get; set; }

            public TelemetryBaseData BaseData { get; } = new TelemetryBaseData();
        }

        private class TelemetryException
        {
            public string TypeName { get; set; }

            public string Message { get; set; }

            public bool HasFullStack { get; set; }
        }

        private class TelemetryBaseData
        {
            public string Id { get; set; }

            public string Name { get; set; }

            public TimeSpan Duration { get; set; }

            public bool? Success { get; set; }

            public string Ver { get; set; }

            public string Message { get; set; }

            public string SeverityLevel { get; set; }

            public TelemetryException[] Exceptions { get; set; }

            public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        }

        private class QuickPulsePayload
        {
            public string Instance { get; set; }

            public DateTime Timestamp { get; set; }

            public string StreamId { get; set; }

            public QuickPulseMetric[] Metrics { get; set; }
        }

        private class QuickPulseMetric
        {
            public string Name { get; set; }

            public double Value { get; set; }

            public int Weight { get; set; }
        }
    }
}
