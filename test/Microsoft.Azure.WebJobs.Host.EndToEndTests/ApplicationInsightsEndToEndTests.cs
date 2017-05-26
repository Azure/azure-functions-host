// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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

        [Theory(Skip ="Compression failure")]
        [InlineData(LogLevel.None, 0)]
        [InlineData(LogLevel.Information, 18)]
        [InlineData(LogLevel.Warning, 10)]
        public async Task QuickPulse_Works_EvenIfFiltered(LogLevel defaultLevel, int expectedTelemetryItems)
        {
            LogCategoryFilter filter = new LogCategoryFilter();
            filter.DefaultLevel = defaultLevel;

            var loggerFactory = new LoggerFactory()
                .AddApplicationInsights(new TestTelemetryClientFactory(_mockApplicationInsightsKey, filter.Filter));

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

                    var methodInfo = GetType().GetMethod(nameof(TestApplicationInsights), BindingFlags.Public | BindingFlags.Static);

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
        public static void TestApplicationInsights(TraceWriter trace, ILogger logger)
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
            public TestTelemetryClientFactory(string instrumentationKey, Func<string, LogLevel, bool> filter)
                : base(instrumentationKey, filter)
            {
            }

            protected override ITelemetryChannel CreateTelemetryChannel()
            {
                ITelemetryChannel channel = base.CreateTelemetryChannel();
                channel.EndpointAddress = _mockApplicationInsightsUrl;

                // DeveloperMode prevents buffering so items are sent immediately.
                channel.DeveloperMode = true;
                
                return channel;
            }
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

        private class TelemetryBaseData
        {
            public string Id { get; set; }

            public string Name { get; set; }

            public TimeSpan Duration { get; set; }

            public bool? Success { get; set; }

            public string Ver { get; set; }

            public string Message { get; set; }

            public string SeverityLevel { get; set; }

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
