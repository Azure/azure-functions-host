// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal class WorkerConfigurationResolverOptionsSetup : IConfigureOptions<WorkerConfigurationResolverOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IEnvironment _environment;

        public WorkerConfigurationResolverOptionsSetup(IConfiguration configuration, IEnvironment environment)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public void Configure(WorkerConfigurationResolverOptions options)
        {
            options.WorkerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);
            options.ReleaseChannel = EnvironmentExtensions.GetPlatformReleaseChannel(_environment);
            options.IsPlaceholderModeEnabled = _environment.IsPlaceholderModeEnabled();
            options.IsMultiLanguageWorkerEnvironment = _environment.IsMultiLanguageRuntimeEnvironment();

            options.LanguageSection = _configuration.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}");
            options.WorkersDirPath = WorkerConfigurationHelper.GetWorkersDirPath(options.LanguageSection);
        }
    }
}