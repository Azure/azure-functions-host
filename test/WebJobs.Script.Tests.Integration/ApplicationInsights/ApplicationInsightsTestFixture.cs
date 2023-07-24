// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsTestFixture : IDisposable
    {
        public const string ApplicationInsightsKey = "some_key";

        public ApplicationInsightsTestFixture(string scriptRoot, string testId, bool ignoreAppInsightsFromWorker = false)
        {
            string scriptPath = Path.Combine(Environment.CurrentDirectory, scriptRoot);
            string logPath = Path.Combine(Path.GetTempPath(), @"Functions");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, testId);

            WebHostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = scriptPath,
                LogPath = logPath,
                SecretsPath = Environment.CurrentDirectory // not used
            };

            TestHost = new TestFunctionHost(scriptPath, logPath,
                configureScriptHostServices: s =>
                {
                    s.AddSingleton<ITelemetryChannel>(_ => Channel);
                    s.Configure<ScriptJobHostOptions>(o =>
                    {
                        o.Functions = new[]
                        {
                            "Scenarios",
                            "HttpTrigger-Scenarios"
                        };
                    });
                    s.AddSingleton<IMetricsLogger>(_ => MetricsLogger);
                },
                configureScriptHostAppConfiguration: configurationBuilder =>
                {
                    configurationBuilder.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        [EnvironmentSettingNames.AppInsightsInstrumentationKey] = ApplicationInsightsKey
                    });
                },
                configureWebHostServices: s =>
                {
                    if (ignoreAppInsightsFromWorker)
                    {
                        s.AddSingleton<IRpcWorkerChannelFactory, TestGrpcWorkerChannelFactory>();
                    }
                });

            HttpClient = TestHost.HttpClient;

            TestHelpers.WaitForWebHost(HttpClient);
        }

        public TestTelemetryChannel Channel { get; private set; } = new TestTelemetryChannel();

        public TestMetricsLogger MetricsLogger { get; private set; } = new TestMetricsLogger();

        public TestFunctionHost TestHost { get; }

        public ScriptApplicationHostOptions WebHostOptions { get; private set; }

        public HttpClient HttpClient { get; private set; }

        public void Dispose()
        {
            TestHost?.Dispose();
            HttpClient?.Dispose();

            // App Insights takes 2 seconds to flush telemetry and because our container
            // is disposed on a background task, it doesn't block. So waiting here to ensure
            // everything is flushed and can't affect subsequent tests.
            Thread.Sleep(2000);
        }

        private class TestGrpcWorkerChannelFactory : GrpcWorkerChannelFactory
        {
            public TestGrpcWorkerChannelFactory(IScriptEventManager eventManager, IEnvironment environment, ILoggerFactory loggerFactory, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IRpcWorkerProcessFactory rpcWorkerProcessManager, ISharedMemoryManager sharedMemoryManager, IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions, IOptions<FunctionsHostingConfigOptions> hostingConfigOptions, IHttpProxyService httpProxyService)
                : base(eventManager, environment, loggerFactory, applicationHostOptions, rpcWorkerProcessManager, sharedMemoryManager, workerConcurrencyOptions, hostingConfigOptions, httpProxyService)
            {
            }

            internal override IRpcWorkerChannel CreateInternal(string workerId, IScriptEventManager eventManager, RpcWorkerConfig languageWorkerConfig, IWorkerProcess rpcWorkerProcess,
            ILogger workerLogger, IMetricsLogger metricsLogger, int attemptCount, IEnvironment environment, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
            ISharedMemoryManager sharedMemoryManager, IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions, IOptions<FunctionsHostingConfigOptions> hostingConfigOptions, IHttpProxyService httpProxyService)
            {
                return new TestGrpcWorkerChannel(workerId, eventManager, languageWorkerConfig, rpcWorkerProcess, workerLogger, metricsLogger,
                    attemptCount, environment, applicationHostOptions, sharedMemoryManager, workerConcurrencyOptions, hostingConfigOptions, httpProxyService);
            }

            private class TestGrpcWorkerChannel : GrpcWorkerChannel
            {
                internal TestGrpcWorkerChannel(string workerId, IScriptEventManager eventManager, RpcWorkerConfig workerConfig, IWorkerProcess rpcWorkerProcess, ILogger logger, IMetricsLogger metricsLogger, int attemptCount, IEnvironment environment, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, ISharedMemoryManager sharedMemoryManager, IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions, IOptions<FunctionsHostingConfigOptions> hostingConfigOptions, IHttpProxyService httpProxyService)
                    : base(workerId, eventManager, workerConfig, rpcWorkerProcess, logger, metricsLogger, attemptCount, environment, applicationHostOptions, sharedMemoryManager, workerConcurrencyOptions, hostingConfigOptions, httpProxyService)
                {   
                }

                internal override void UpdateCapabilities(IDictionary<string, string> fields, GrpcCapabilitiesUpdateStrategy strategy)
                {
                    // inject a capability
                    fields[RpcWorkerConstants.WorkerApplicationInsightsLoggingEnabled] = bool.TrueString;
                    base.UpdateCapabilities(fields, strategy);
                }
            }
        }
    }
}