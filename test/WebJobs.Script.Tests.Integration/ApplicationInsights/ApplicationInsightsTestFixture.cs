// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsTestFixture : IDisposable
    {
        private const string _mockApplicationInsightsUrl = "http://localhost:4005/v2/track/";
        private const string _mockApplicationInsightsKey = "some_key";

        private readonly HttpListener _applicationInsightsListener = new HttpListener();
        private Thread _listenerThread;

        static ApplicationInsightsTestFixture()
        {
            // We need to set this to something in order to trigger App Insights integration. But since
            // we're hitting a local HttpListener, it can be anything.
            ScriptSettingsManager.Instance.ApplicationInsightsInstrumentationKey = _mockApplicationInsightsKey;
        }

        public ApplicationInsightsTestFixture(string scriptRoot, string testId)
        {
            _settingsManager = ScriptSettingsManager.Instance;

            HostSettings = new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Environment.CurrentDirectory, scriptRoot),
                LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                SecretsPath = Environment.CurrentDirectory, // not used
                LoggerFactoryBuilder = new TestLoggerFactoryBuilder(Channel),
                IsAuthDisabled = true
            };
            WebApiConfig.Register(_config, _settingsManager, HostSettings);

            var resolver = _config.DependencyResolver;
            var hostConfig = resolver.GetService<WebHostResolver>().GetScriptHostConfiguration(HostSettings);

            _settingsManager.ApplicationInsightsInstrumentationKey = TestChannelLoggerFactoryBuilder.ApplicationInsightsKey;

            InitializeConfig(hostConfig);

            _httpServer = new HttpServer(_config);
            HttpClient = new HttpClient(_httpServer)
            {
                BaseAddress = new Uri("https://localhost/")
            };

            TestHelpers.WaitForWebHost(HttpClient);
        }

        public List<TelemetryPayload> TelemetryItems { get; } = new List<TelemetryPayload>();

        protected override void InitializeConfig(ScriptHostConfiguration config)
        {
            var builder = new TestLoggerFactoryBuilder();
            config.HostConfig.AddService<ILoggerFactoryBuilder>(builder);

        public HttpClient HttpClient { get; private set; }

        protected void InitializeConfig(ScriptHostConfiguration config)
        {
            config.OnConfigurationApplied = c =>
            {
                // turn this off as it makes validation tough
                config.HostConfig.Aggregator.IsEnabled = false;

                // Overwrite the generated function whitelist to only include two functions.
                c.Functions = new[] { "Scenarios", "HttpTrigger-Scenarios" };
            };

            StartListening();
        }

        public void StartListening()
        {
            _applicationInsightsListener.Prefixes.Add(_mockApplicationInsightsUrl);
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

                if (!string.IsNullOrWhiteSpace(request.Headers["Content-Encoding"]) &&
                    string.Equals("gzip", request.Headers["Content-Encoding"],
                        StringComparison.InvariantCultureIgnoreCase))
                {
                    result = Decompress(result);
                }

                var newLines = new[] { "\r\n", "\n" };

                string[] lines = result.Split(newLines, StringSplitOptions.RemoveEmptyEntries);

                TelemetryPayload telemetry = JsonConvert.DeserializeObject<TelemetryPayload>(result);
                TelemetryItems.Add(telemetry);
            }
            finally
            {
                response.Close();
            }
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

        public override void Dispose()
        {
            base.Dispose();
            _applicationInsightsListener.Stop();
            _listenerThread.Join();
        }

        private class TestLoggerFactoryBuilder : DefaultLoggerFactoryBuilder
        {
            public override void AddLoggerProviders(ILoggerFactory factory, ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager)
            {
                // Replace TelemetryClient
                var clientFactory = new TestTelemetryClientFactory(_mockApplicationInsightsUrl, _mockApplicationInsightsKey, scriptConfig.LogFilter.Filter);
                scriptConfig.HostConfig.AddService<ITelemetryClientFactory>(clientFactory);

                base.AddLoggerProviders(factory, scriptConfig, settingsManager);
            }
        }

        private class TestTelemetryClientFactory : ScriptTelemetryClientFactory
        {
            private string _channelUrl;

            public TestTelemetryClientFactory(string channelUrl, string instrumentationKey, Func<string, LogLevel, bool> filter)
                : base(instrumentationKey, filter)
            {
                _channelUrl = channelUrl;
            }

            protected override ITelemetryChannel CreateTelemetryChannel()
            {
                ITelemetryChannel channel = base.CreateTelemetryChannel();
                channel.EndpointAddress = _channelUrl;

                // DeveloperMode prevents buffering so items are sent immediately.
                channel.DeveloperMode = true;

                return channel;
            }
        }

        public void Dispose()
        {
            _httpServer?.Dispose();
            HttpClient?.Dispose();
        }

        private class TestLoggerFactoryBuilder : DefaultLoggerFactoryBuilder
        {
            private readonly TestTelemetryChannel _channel;

            public TestLoggerFactoryBuilder(TestTelemetryChannel channel)
            {
                _channel = channel;
            }

            public override void AddLoggerProviders(ILoggerFactory factory, ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager)
            {
                // Replace TelemetryClient
                var clientFactory = new TestTelemetryClientFactory(scriptConfig.LogFilter.Filter, _channel);
                scriptConfig.HostConfig.AddService<ITelemetryClientFactory>(clientFactory);

                base.AddLoggerProviders(factory, scriptConfig, settingsManager);
            }
        }

        public class TestTelemetryChannel : ITelemetryChannel
        {
            public IList<ITelemetry> Telemetries { get; private set; } = new List<ITelemetry>();

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

        private class TestTelemetryClientFactory : ScriptTelemetryClientFactory
        {
            private TestTelemetryChannel _channel;

            public TestTelemetryClientFactory(Func<string, LogLevel, bool> filter, TestTelemetryChannel channel)
                : base(TestChannelLoggerFactoryBuilder.ApplicationInsightsKey, new SamplingPercentageEstimatorSettings(), filter)
            {
                _channel = channel;
            }

            protected override ITelemetryChannel CreateTelemetryChannel()
            {
                return _channel;
            }
        }
    }
}
