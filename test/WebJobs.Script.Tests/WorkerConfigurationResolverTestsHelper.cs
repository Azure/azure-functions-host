// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    internal static class WorkerConfigurationResolverTestsHelper
    {
        internal static IOptionsMonitor<WorkerConfigurationResolverOptions> GetTestWorkerConfigurationResolverOptions(IConfiguration configuration,
                                        IEnvironment environment,
                                        IScriptHostManager scriptHostManager,
                                        IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions)
        {
            var resolverOptionssetup = new WorkerConfigurationResolverOptionsSetup(configuration, environment, scriptHostManager, functionsHostingConfigOptions);
            var resolverOptions = new WorkerConfigurationResolverOptions();
            resolverOptionssetup.Configure(resolverOptions);

            var factory = new TestOptionsFactory<WorkerConfigurationResolverOptions>(resolverOptions);
            var source = new TestChangeTokenSource<WorkerConfigurationResolverOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<WorkerConfigurationResolverOptions>(factory, changeTokens, factory);

            return optionsMonitor;
        }
    }
}
