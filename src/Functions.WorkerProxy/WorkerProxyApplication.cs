// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Azure.Functions.WorkerProxy.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy;

internal static class WorkerProxyApplication
{
    private const string AllowedHostsConfigurationKey = "AllowedHosts";
    private const string KestrelConfigurationSection = "WorkerProxy:Kestrel";
    private const string ReadyPath = "/admin/instance/ready";

    private static readonly Dictionary<string, string?> HostingConfigurationOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [AllowedHostsConfigurationKey] = "*",
            [WebHostDefaults.ServerUrlsKey] = string.Empty,
            [WebHostDefaults.PreferHostingUrlsKey] = bool.FalseString
        };

    private static readonly Dictionary<string, string> CommandLineMappings =
        new(StringComparer.Ordinal)
        {
            [WorkerProxyOptions.ManagementPortCommandLineName] =
                WorkerProxyOptions.ManagementPortConfigurationKey
        };

    public static WebApplication Build(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return Build(configuration =>
        {
            configuration.AddEnvironmentVariables();
            AddCommandLineConfiguration(configuration, args);
        });
    }

    internal static WebApplication Build(Action<ConfigurationManager> configureExternalConfiguration)
    {
        ArgumentNullException.ThrowIfNull(configureExternalConfiguration);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.Configuration.Sources.Clear();
        configureExternalConfiguration(builder.Configuration);

        // WorkerProxy owns its listener explicitly. Generic hosting configuration must not add
        // endpoints, move the management listener, or reject the platform readiness probe.
        builder.Configuration.AddInMemoryCollection(HostingConfigurationOverrides);

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

    internal static void AddCommandLineConfiguration(
        ConfigurationManager configuration, string[] args)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(args);

        configuration.AddCommandLine(args, CommandLineMappings);
    }
}
