// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Hosting
{
    public static class TestWebHostBuilderExtensions
    {
        /// <summary>
        /// Registers a callback to configure the WebJobBuilder on the ScriptHost. Use this to explicitly register any bindings. If you want
        /// to override services, use <see cref="ConfigureScriptHost" /> instead.
        /// </summary>
        public static IWebHostBuilder ConfigureScriptHostWebJobsBuilder(this IWebHostBuilder webHostBuilder, Action<IWebJobsBuilder> builder) =>
            webHostBuilder.ConfigureServices(s => s.AddSingleton<IConfigureBuilder<IWebJobsBuilder>>(_ => new DelegatedConfigureBuilder<IWebJobsBuilder>(builder)));

        /// <summary>
        /// Registers a callback to configure app configuration on the ScriptHost.
        /// </summary>
        public static IWebHostBuilder ConfigureScriptHostAppConfiguration(this IWebHostBuilder webHostBuilder, Action<IConfigurationBuilder> builder) =>
            webHostBuilder.ConfigureServices(s => s.AddSingleton<IConfigureBuilder<IConfigurationBuilder>>(_ => new DelegatedConfigureBuilder<IConfigurationBuilder>(builder)));

        /// <summary>
        /// Registers a callback to configure logging on the ScriptHost.
        /// </summary>
        public static IWebHostBuilder ConfigureScriptHostLogging(this IWebHostBuilder webHostBuilder, Action<ILoggingBuilder> builder) =>
            webHostBuilder.ConfigureServices(s => s.AddSingleton<IConfigureBuilder<ILoggingBuilder>>(_ => new DelegatedConfigureBuilder<ILoggingBuilder>(builder)));

        /// <summary>
        /// Registers a callback to configure services on the ScriptHost. This callback is the last call to register services,
        /// which allows you to override anything set by the host.
        /// </summary>
        public static IWebHostBuilder ConfigureScriptHostServices(this IWebHostBuilder webHostBuilder, Action<IServiceCollection> builder) =>
            webHostBuilder.ConfigureServices(s => s.AddSingleton<IConfigureBuilder<IServiceCollection>>(_ => new DelegatedConfigureBuilder<IServiceCollection>(builder)));
    }
}
