// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Composition;
using Microsoft.Azure.WebJobs.Script.WebHost.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultScriptHostBuilder : IScriptHostBuilder
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IWorkerComposition _composition;
        private readonly IServiceProvider _rootServiceProvider;
        private readonly IServiceCollection _rootServices;

        public DefaultScriptHostBuilder(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IServiceCollection rootServices, IServiceProvider rootServiceProvider)
        {
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
            _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));
            _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
            _composition = rootServiceProvider.GetService<SelectedWorkerComposition>()?.Value
                ?? ServerWorkerComposition.Instance;
        }

        public IHost BuildHost(bool skipHostStartup, bool skipHostConfigurationParsing)
        {
            var builder = new HostBuilder();

            if (skipHostStartup)
            {
                builder.Properties[ScriptConstants.SkipHostInitializationKey] = bool.TrueString;
            }

            if (skipHostConfigurationParsing)
            {
                builder.ConfigureAppConfiguration((context, _) =>
                {
                    context.Properties[ScriptConstants.SkipHostJsonConfigurationKey] = true;
                });
            }

            builder.SetAzureFunctionsEnvironment()
                .AddWebScriptHost(
                    _rootServiceProvider,
                    _rootServices,
                    _applicationHostOptions.CurrentValue,
                    configureWebJobs: null,
                    composition: _composition);

            if (skipHostStartup)
            {
                builder.ConfigureServices(services =>
                {
                    // When skipping host startup (e.g. offline), we need most general services registered so admin
                    // APIs can function. However, we want to prevent the ScriptHost from
                    // actually starting up. To accomplish this, we remove the host service
                    // responsible for starting the job host.
                    var jobHostService = services.FirstOrDefault(p => p.ImplementationType == typeof(JobHostService));
                    services.Remove(jobHostService);
                });
            }

            return builder.Build();
        }
    }
}
