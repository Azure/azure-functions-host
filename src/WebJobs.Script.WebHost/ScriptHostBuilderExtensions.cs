// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class ScriptHostBuilderExtensions
    {
        public static IHostBuilder AddScriptHostServices(this IHostBuilder builder)
        {
            builder.ConfigureServices(c =>
            {
                c.AddSingleton<ScriptHost>();
                c.AddSingleton<IScriptJobHost>(p => p.GetRequiredService<ScriptHost>());
                c.AddSingleton<IJobHost>(p => p.GetRequiredService<ScriptHost>());
            });

            return builder;
        }
    }
}
