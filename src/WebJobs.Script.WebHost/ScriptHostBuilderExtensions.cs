// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class ScriptHostBuilderExtensions
    {
        public static IHostBuilder AddScriptHost(this IHostBuilder builder, IOptions<ScriptWebHostOptions> webHostOptions)
        {
            builder.ConfigureServices(services =>
            {
                // Core WebJobs/Script Host services
                services.AddSingleton<ScriptHost>();
                services.AddSingleton<IScriptJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IFunctionMetadataManager, FunctionMetadataManager>();
                services.AddSingleton<ITypeLocator, ScriptTypeLocator>();
                services.AddSingleton<WebJobs.Host.Executors.IHostIdProvider, IdProvider>();
                services.AddSingleton<ScriptSettingsManager>();
                // TODO: DI (FACAVAL) Review metrics logger registration
                services.AddSingleton<IMetricsLogger, WebHostMetricsLogger>();
                services.AddSingleton<IScriptEventManager, ScriptEventManager>();

                // Script binding providers
                services.AddSingleton<IScriptBindingProvider, WebJobsCoreScriptBindingProvider>();
                services.AddSingleton<IScriptBindingProvider, CoreExtensionsScriptBindingProvider>();
                services.AddSingleton<IScriptBindingProvider, GeneralScriptBindingProvider>();

                // Secret management
                services.AddSingleton<ISecretManager>(c => c.GetService<ISecretManagerFactory>().Create());
                services.AddSingleton<ISecretsRepository>(c => c.GetService<ISecretsRepositoryFactory>().Create());
                services.AddSingleton<ISecretManagerFactory, DefaultSecretManagerFactory>();
                services.AddSingleton<ISecretsRepositoryFactory, DefaultSecretsRepositoryFactory>();

                // Configuration
                services.AddSingleton<IOptions<ScriptWebHostOptions>>(webHostOptions);
                services.ConfigureOptions<ScriptHostOptionsSetup>();
            });

            return builder;
        }

        public static IHostBuilder UseScriptExternalStartup(this IHostBuilder builder, string rootScriptPath)
        {
            return builder.UseExternalStartup(new ScriptStartupTypeDiscoverer(rootScriptPath));
        }
    }
}
