// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    internal static class WorkerConfigurationResolverTestsHelper
    {
        internal static IOptionsMonitor<WorkerConfigurationResolverOptions> GetTestWorkerConfigurationResolverOptions(IConfiguration configuration,
                                        IEnvironment environment,
                                        IScriptHostManager scriptHostManager,
                                        IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions = null)
        {
            if (functionsHostingConfigOptions is null)
            {
                var hostingOptions = new FunctionsHostingConfigOptions();
                functionsHostingConfigOptions = new OptionsWrapper<FunctionsHostingConfigOptions>(new FunctionsHostingConfigOptions());
            }

            var resolverOptionsSetup = new WorkerConfigurationResolverOptionsSetup(configuration, environment, scriptHostManager, functionsHostingConfigOptions);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionsSetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            return optionsMonitor;
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

        internal static LoggerFactory GetTestLoggerFactory()
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            return loggerFactory;
        }
    }
}
