// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace Microsoft.WebJobs.Script.Tests
{
    public static class TestHostBuilderExtensions
    {
        public static IHostBuilder ConfigureDefaultTestScriptHost(this IHostBuilder builder, Action<ScriptWebHostOptions> configure = null, bool runStartupHostedServices = false)
        {
            var webHostOptions = new ScriptWebHostOptions();

            configure?.Invoke(webHostOptions);

            // Register root services
            var services = new ServiceCollection();
            AddMockedSingleton<IScriptHostManager>(services);
            AddMockedSingleton<IScriptWebHostEnvironment>(services);
            AddMockedSingleton<IWebJobsRouter>(services);
            AddMockedSingleton<IEventGenerator>(services);
            AddMockedSingleton<AspNetCore.Hosting.IApplicationLifetime>(services);

            var rootProvider = new WebHostServiceProvider(services);

            builder.AddScriptHost(rootProvider, rootProvider, new OptionsWrapper<ScriptWebHostOptions>(webHostOptions));

            if (!runStartupHostedServices)
            {
                builder.ConfigureServices(s => s.RemoveAll<IHostedService>());
            }

            return builder;
        }

        private static IServiceCollection AddMockedSingleton<T>(IServiceCollection services) where T : class
        {
            var mock = new Mock<T>();
            return services.AddSingleton<T>(mock.Object);
        }
    }
}
