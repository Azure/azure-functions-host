// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.Functions.WorkerProxy.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.Functions.WorkerProxy;

internal static class WorkerProxyApplication
{
    private const string AllowedHostsConfigurationKey = "AllowedHosts";
    private const string KestrelConfigurationSection = "WorkerProxy:Kestrel";
    private const string ReadyPath = "/admin/instance/ready";

    private static readonly Dictionary<string, string> CommandLineMappings =
        new(StringComparer.Ordinal)
        {
            [WorkerProxyOptions.ManagementPortCommandLineName] =
                WorkerProxyOptions.ManagementPortConfigurationKey
        };

    public static WebApplication Build(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddEnvironmentVariables();
        builder.Configuration.AddCommandLine(args, CommandLineMappings);
        builder.Configuration.AddInMemoryCollection();

        // WorkerProxy owns its listener explicitly. Generic hosting configuration must not add
        // endpoints, move the management listener, or reject the platform readiness probe.
        builder.Configuration[AllowedHostsConfigurationKey] = "*";
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.WebHost.UseSetting(WebHostDefaults.PreferHostingUrlsKey, bool.FalseString);

        builder.Services
            .AddOptions<WorkerProxyOptions>()
            .Bind(builder.Configuration)
            .Validate(
                options => options.ManagementPort is >= 1 and <= 65535,
                "Management port must be between 1 and 65535.")
            .ValidateOnStart();

        builder.Services
            .AddOptions<KestrelServerOptions>()
            .Configure<IOptions<WorkerProxyOptions>, IConfiguration>(
                (kestrelOptions, workerProxyOptions, configuration) =>
            {
                kestrelOptions.Configure(
                    configuration.GetSection(KestrelConfigurationSection),
                    reloadOnChange: false);
                kestrelOptions.ListenAnyIP(workerProxyOptions.Value.ManagementPort, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1;
                });
            });

        WebApplication app = builder.Build();
        app.MapGet(ReadyPath, static () => TypedResults.Ok()).AllowAnonymous();

        return app;
    }
}
