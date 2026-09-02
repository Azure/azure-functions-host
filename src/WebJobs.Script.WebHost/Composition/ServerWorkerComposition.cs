// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Composition;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Composition;

internal sealed class ServerWorkerComposition : IWorkerComposition
{
    private ServerWorkerComposition()
    {
    }

    public static ServerWorkerComposition Instance { get; } = new();

    public void ConfigureWebHostServices(IServiceCollection services, IMvcBuilder mvcBuilder)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(mvcBuilder);

        services.AddScriptGrpc();
        services.AddCommonRpcServices();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<WebJobsScriptHostService>());
    }

    public void ConfigureScriptHostServices(IServiceCollection services, IServiceProvider rootServiceProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rootServiceProvider);

        services.AddRpcScriptHostServices();
    }
}
