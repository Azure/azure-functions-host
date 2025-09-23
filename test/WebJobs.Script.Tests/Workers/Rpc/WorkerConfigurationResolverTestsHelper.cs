// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
                functionsHostingConfigOptions = new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions);
            }

            var testLoggerFactory = GetTestLoggerFactory();
            var resolverOptionsSetup = new WorkerConfigurationResolverOptionsSetup(testLoggerFactory, configuration, scriptHostManager, FileUtility.Instance);
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
    }
}
