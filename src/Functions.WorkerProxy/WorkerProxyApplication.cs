// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Builds the WorkerProxy management application and its hosted FunctionRpc relay listeners.
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

        // Endpoint selection intentionally follows standard ASP.NET Core configuration and
        // precedence. For example, URLS overrides HTTP_PORTS when both are present.
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args });
        builder.Services.AddSingleton(static services => FunctionRpcRelayOptions.FromConfiguration(services.GetRequiredService<IConfiguration>()));
        builder.Services.AddSingleton<FunctionRpcRelay>();
        builder.Services.AddSingleton<FunctionRpcRelayServer>();
        builder.Services.AddHostedService(static services => services.GetRequiredService<FunctionRpcRelayServer>());

        WebApplication app = builder.Build();
        app.MapGet(ReadyPath, static () => TypedResults.Ok()).AllowAnonymous();

        return app;
    }
}
