// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Rpc.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering shared RPC ScriptHost services.
/// </summary>
public static class RpcScriptHostCoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds the RPC services shared by all ScriptHost channel topologies.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddRpcScriptHostCoreServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureOptions<HttpWorkerOptionsSetup>();
        services.ConfigureOptions<RpcFunctionMetadataOptionsSetup>();
        services.ConfigureOptions<RpcScriptHostRecycleOptionsSetup>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FunctionInvocationDispatcherShutdownManager>());

        services.AddSingleton<IWorkerFunctionDescriptorProviderFactory, RpcWorkerFunctionDescriptorProviderFactory>();
        services.AddSingleton<IScriptHostLifecycleService, RpcScriptHostLifecycleService>();

        return services;
    }
}
