// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
        // WorkerProxy owns all listener bindings and does not use the generic ASP.NET Core URL configuration.
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.WebHost.UseSetting(WebHostDefaults.HttpPortsKey, string.Empty);
        builder.WebHost.UseSetting(WebHostDefaults.HttpsPortsKey, string.Empty);
        builder.WebHost.UseSetting(WebHostDefaults.PreferHostingUrlsKey, bool.FalseString);
        builder.Services.AddOptions<WorkerProxyOptions>().BindConfiguration(WorkerProxyOptions.SectionName).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<WorkerProxyOptions>, WorkerProxyOptionsValidator>();
        builder.Services.AddSingleton<WorkerProxyEndpointConfiguration>();
        builder.Services.AddSingleton<IConfigureOptions<KestrelServerOptions>>(
            static services => services.GetRequiredService<WorkerProxyEndpointConfiguration>());
        builder.Services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = int.MaxValue;
            options.MaxSendMessageSize = int.MaxValue;
        });
        builder.Services.AddSingleton<FunctionRpcRelay>();
        builder.Services.AddHostedService(static services => services.GetRequiredService<FunctionRpcRelay>());
        ConfigureHttpForwarding(builder);

        WebApplication app = builder.Build();
        WorkerProxyEndpointConfiguration endpoints = app.Services.GetRequiredService<WorkerProxyEndpointConfiguration>();
        app.MapWhen(context => endpoints.IsManagementPort(context.Connection.LocalPort), ConfigureManagementPipeline);
        app.MapWhen(context => endpoints.TryGetRelaySide(context.Connection.LocalPort, out _), ConfigureGrpcPipeline);
        app.MapWhen(context => endpoints.IsHttpPort(context.Connection.LocalPort), ConfigureHttpPipeline);
        app.Run(static context => Results.NotFound().ExecuteAsync(context));

        return app;
    }

    private static void ConfigureManagementPipeline(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(static endpoints =>
        {
            endpoints.MapGet(ReadyPath, static () => Results.Ok()).AllowAnonymous();
        });
    }

    private static void ConfigureGrpcPipeline(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(static endpoints =>
        {
            endpoints.MapGrpcService<FunctionRpcRelayService>();
        });
    }

    private static void ConfigureHttpPipeline(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(static endpoints =>
        {
            // Admin paths are reserved for the platform. We intentionally block them to make that clear.
            endpoints.Map("/admin/{**path}", static () => Results.NotFound()).AllowAnonymous();
            endpoints.Map(
                "/{**path}",
                ForwardHttpRequestAsync)
                .AllowAnonymous();
        });
    }

    private static Task ForwardHttpRequestAsync(
        HttpContext context, WorkerHttpForwardingMiddleware middleware)
    {
        IHttpMaxRequestBodySizeFeature requestBodySizeFeature =
            context.Features.Get<IHttpMaxRequestBodySizeFeature>()
            ?? throw new InvalidOperationException(
                "Unable to disable the forwarded request body size limit because IHttpMaxRequestBodySizeFeature is unavailable.");

        if (requestBodySizeFeature.IsReadOnly)
        {
            throw new InvalidOperationException(
                "Unable to disable the forwarded request body size limit after the request body has been read.");
        }

        requestBodySizeFeature.MaxRequestBodySize = null;

        return middleware.InvokeAsync(context);
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
                ActivityHeadersPropagator = null,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                UseProxy = false
            });

        builder.Services.AddSingleton<WorkerHttpForwarder>();
        builder.Services.AddSingleton<WorkerHttpForwardingMiddleware>();
    }
}
