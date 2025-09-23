// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    internal static class WorkerConfigurationResolverTestsHelper
    {
        internal static IOptionsMonitor<WorkerConfigurationResolverOptions> GetTestWorkerConfigurationResolverOptions(IConfiguration configuration,
                                        string workerRuntime,
                                        IEnvironment environment,
                                        IScriptHostManager scriptHostManager,
                                        IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions = null)
        {
            if (functionsHostingConfigOptions is null)
            {
                var hostingOptions = new FunctionsHostingConfigOptions();
                functionsHostingConfigOptions = new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions);
            }

            var mockWorkerRuntimeResolver = new Mock<IWorkerRuntimeResolver>(MockBehavior.Strict);
            mockWorkerRuntimeResolver.Setup(r => r.GetWorkerRuntime(It.IsAny<string>())).Returns(workerRuntime);

            var testLoggerFactory = GetTestLoggerFactory();
            var resolverOptionsSetup = new WorkerConfigurationResolverOptionsSetup(
                testLoggerFactory,
                configuration,
                environment,
                FileUtility.Instance,
                scriptHostManager,
                functionsHostingConfigOptions,
                mockWorkerRuntimeResolver.Object);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionsSetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            return optionsMonitor;
        }

        internal static LoggerFactory GetTestLoggerFactory()
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            return loggerFactory;
        }

        internal static IConfiguration GetConfigurationWithProbingPaths(List<string> probingPaths)
        {
            var jsonObj = new
            {
                languageWorkers = new
                {
                    probingPaths
                }
            };

            var jsonString = JsonSerializer.Serialize(jsonObj, new JsonSerializerOptions { WriteIndented = true });
            var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonString));

            var configurationBuilder = new ConfigurationBuilder()
                .Add(new ScriptEnvironmentVariablesConfigurationSource())
                .AddJsonStream(jsonStream);

            return configurationBuilder.Build();
        }

        internal static IEnumerable<IWorkerConfigurationProvider> GetProviders(ILoggerFactory loggerFactory,
                                                ILogger<DynamicWorkerConfigurationProvider> dynamicLogger,
                                                IMetricsLogger metricsLogger,
                                                IFileSystem fileSystem,
                                                IWorkerProfileManager workerProfileManager,
                                                ISystemRuntimeInformation systemRuntimeInformation,
                                                IOptionsMonitor<WorkerConfigurationResolverOptions> optionsMonitor)
        {
            return new List<IWorkerConfigurationProvider>
                {
                    new DefaultWorkerConfigurationProvider(loggerFactory, metricsLogger, FileUtility.Instance, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor),
                    new DynamicWorkerConfigurationProvider(dynamicLogger, metricsLogger, FileUtility.Instance, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor),
                    new ExplicitWorkerConfigurationProvider(loggerFactory, metricsLogger, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor),
                };
        }
    }
}
