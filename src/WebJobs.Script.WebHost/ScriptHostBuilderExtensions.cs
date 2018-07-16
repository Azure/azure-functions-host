// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class ScriptHostBuilderExtensions
    {
        public static IHostBuilder AddScriptHostServices(this IHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ScriptHost>();
                services.AddSingleton<IScriptJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IJobHost>(p => p.GetRequiredService<ScriptHost>());

                // Configuration
                services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<ScriptWebHostOptions>, ScriptWebHostOptionsSetup>());
                services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<ScriptHostOptions>, ScriptHostOptionsSetup>());
            });

            return builder;
        }
    }
}
