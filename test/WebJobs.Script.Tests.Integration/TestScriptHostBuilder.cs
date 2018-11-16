using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration
{
    internal class TestScriptHostBuilder : IScriptHostBuilder
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IServiceProvider _rootServiceProvider;
        private readonly IServiceScopeFactory _rootScopeFactory;
        private readonly Action<HostBuilder> _configureTestServices;

        public TestScriptHostBuilder(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IServiceProvider rootServiceProvider,
            IServiceScopeFactory rootScopeFactory, Action<HostBuilder> configureTestServices)
        {
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
            _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));
            _rootScopeFactory = rootScopeFactory ?? throw new ArgumentNullException(nameof(rootScopeFactory));
            _configureTestServices = configureTestServices ?? throw new ArgumentNullException(nameof(configureTestServices));
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
                .AddWebScriptHost(_rootServiceProvider, _rootScopeFactory, _applicationHostOptions.CurrentValue);

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

            _configureTestServices(builder);

            return builder.Build();
        }
    }
}
