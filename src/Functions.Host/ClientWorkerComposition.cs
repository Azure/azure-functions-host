// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.Host.Controllers;
using Azure.Functions.Host.WorkerLink;
using Microsoft.Azure.WebJobs.Script.Composition;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Azure.Functions.Host;

/// <summary>
/// Defines the Client-backed worker composition for the separate Functions Host.
/// </summary>
internal sealed class ClientWorkerComposition : IWorkerComposition
{
    private ClientWorkerComposition()
    {
    }

    public static ClientWorkerComposition Instance { get; } = new();

    public void ConfigureWebHostServices(IServiceCollection services, IMvcBuilder mvcBuilder)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(mvcBuilder);

        services.AddRpcClientWebHostServices(static provider => provider.GetRequiredService<WebJobsScriptHostService>());
        services.AddSingleton<IWebHostWorkerManager, ClientWebHostWorkerManager>();
        services.AddSingleton<WorkerLinkConfigurationMonitor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISpecializationWaitExemption, WorkerLinkSpecializationWaitExemption>());
        mvcBuilder.AddApplicationPart(typeof(WorkerLinkController).Assembly);
    }

    public void ConfigureScriptHostServices(IServiceCollection services, IServiceProvider rootServiceProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rootServiceProvider);

        services.AddRpcClientScriptHostServices(rootServiceProvider);
    }
}
