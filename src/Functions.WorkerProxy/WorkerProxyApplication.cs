// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Azure.Functions.WorkerProxy;

internal static class WorkerProxyApplication
{
    private const string ReadyPath = "/admin/instance/ready";

    public static WebApplication Build(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // Endpoint selection intentionally follows standard ASP.NET Core configuration and
        // precedence. For example, URLS overrides HTTP_PORTS when both are present.
        WebApplicationBuilder builder =
            WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args });
        WebApplication app = builder.Build();
        app.MapGet(ReadyPath, static () => TypedResults.Ok()).AllowAnonymous();

        return app;
    }
}
