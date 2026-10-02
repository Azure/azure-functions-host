// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.Host.Controllers;
using Azure.Functions.Host.HostState;
using Microsoft.Azure.WebJobs.Script.Composition;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // ScriptHost children inherit these system-provider rules without changing customer logging filters.
        services.AddLogging(logging =>
        {
            const string categoryPrefix = "Azure.Functions.";

            // Trace is only the filter minimum; event levels are unchanged. SystemLogger requires Debug or higher
            // unless diagnostic mode enables Trace.
            logging.AddFilter<WebHostSystemLoggerProvider>(categoryPrefix, LogLevel.Trace);
            logging.AddFilter<SystemLoggerProvider>(categoryPrefix, LogLevel.Trace);
        });

        services.AddRpcClientWebHostServices(static provider => provider.GetRequiredService<WebJobsScriptHostService>());
        services.AddComputeRuntimeStateServices();
        services.AddAppServerHostStatePublisher();
        services.AddSingleton<IWebHostWorkerManager, ClientWebHostWorkerManager>();

        // Compute separation does not run a placeholder ScriptHost. The ScriptHost starts after the first worker
        // links, so specialization must not wait to restart it.
        services.Configure<StandbyOptions>(static options => options.SupportsPlaceholderScriptHost = false);
        mvcBuilder.AddApplicationPart(typeof(WorkerLinkController).Assembly);
    }

    public void ConfigureScriptHostServices(IServiceCollection services, IServiceProvider rootServiceProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rootServiceProvider);

        services.AddRpcClientScriptHostServices(rootServiceProvider);
    }
}
