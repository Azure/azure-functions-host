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
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsTestFixture : EndToEndTestFixture
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
            : base(scriptRoot, testId)
        {
        }

        public List<TelemetryPayload> TelemetryItems { get; } = new List<TelemetryPayload>();

        protected override void InitializeConfig(ScriptHostConfiguration config)
        {
            var builder = new TestLoggerFactoryBuilder();
            config.HostConfig.AddService<ILoggerFactoryBuilder>(builder);
            var exceptionHandler = new Mock<IWebJobsExceptionHandler>();
            config.HostConfig.AddService<IWebJobsExceptionHandler>(exceptionHandler.Object);

            // turn this off as it makes validation tough
            config.HostConfig.Aggregator.IsEnabled = false;

            config.OnConfigurationApplied = c =>
            {
                // Overwrite the generated function whitelist to only include one function.
                c.Functions = new[] { "Scenarios" };
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
    }
}
