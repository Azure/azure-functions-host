// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using Azure.Functions.WorkerProxy.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Builds the WorkerProxy management and FunctionRpc application.
/// </summary>
internal static class WorkerProxyApplication
{
    private const string ReadyPath = "/admin/instance/ready";

    /// <summary>
    /// Creates the configured WorkerProxy application.
    /// </summary>
    /// <param name="args">The command-line arguments supplied to WorkerProxy.</param>
    /// <returns>The application ready to be started.</returns>
    public static WebApplication Build(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args });
        ConfigureWebHostSettings(builder);
        ConfigureProxyOptions(builder);
        builder.Services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = int.MaxValue;
            options.MaxSendMessageSize = int.MaxValue;
        });
        builder.Services.AddSingleton<FunctionRpcRelay>();
        builder.Services.AddHostedService(static services => services.GetRequiredService<FunctionRpcRelay>());

        ConfigureHttpForwarding(builder);

        WebApplication app = builder.Build();
        app.UseMiddleware<WorkerHttpForwardingMiddleware>();
        app.MapGrpcService<FunctionRpcRelayService>();
        app.MapGet(ReadyPath, static (HttpContext context) =>
        {
            WorkerProxyEndpointConfiguration endpoints = context.RequestServices.GetRequiredService<WorkerProxyEndpointConfiguration>();
            return endpoints.IsManagementPort(context.Connection.LocalPort) ? Results.Ok() : Results.NotFound();
        }).AllowAnonymous();

        return app;
    }

    private static void ConfigureWebHostSettings(WebApplicationBuilder builder)
    {
        // WorkerProxy owns all listener bindings and does not use the generic ASP.NET Core URL configuration.
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.WebHost.UseSetting(WebHostDefaults.HttpPortsKey, string.Empty);
        builder.WebHost.UseSetting(WebHostDefaults.HttpsPortsKey, string.Empty);
        builder.WebHost.UseSetting(WebHostDefaults.PreferHostingUrlsKey, bool.FalseString);
    }

    private static void ConfigureProxyOptions(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<WorkerProxyOptions>()
            .BindConfiguration(WorkerProxyOptions.SectionName)
            .ValidateOnStart();

        builder.Services.AddSingleton<IValidateOptions<WorkerProxyOptions>, WorkerProxyOptionsValidator>();
        builder.Services.AddSingleton<WorkerProxyEndpointConfiguration>();

        builder.Services.AddSingleton<IConfigureOptions<KestrelServerOptions>>(
            static services => services.GetRequiredService<WorkerProxyEndpointConfiguration>());
    }

    private static void ConfigureHttpForwarding(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<WorkerEndpointReadinessProbeOptions>()
            .BindConfiguration(WorkerEndpointReadinessProbeOptions.SectionName)
            .ValidateOnStart();

        builder.Services.AddSingleton<
            IValidateOptions<WorkerEndpointReadinessProbeOptions>,
            WorkerEndpointReadinessProbeOptionsValidator>();

        builder.Services.AddSingleton<WorkerEndpointReadinessProbe>();
        builder.Services.AddHttpForwarder();
        builder.Services.AddHttpClient(nameof(WorkerHttpForwarder))
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                UseProxy = false
            });

        builder.Services.AddSingleton<WorkerHttpForwarder>();
    }
}
