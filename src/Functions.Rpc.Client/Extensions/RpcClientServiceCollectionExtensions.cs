// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Registers the Client transport and channel lifecycle for isolated tests.
/// </summary>
/// <remarks>Production composition must remain absent until compute composition explicitly consumes these registrations.</remarks>
internal static class RpcClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Client lifecycle as root-container singletons.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The supplied service collection.</returns>
    internal static IServiceCollection AddRpcClientServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IRpcClientFactory, RpcClientFactory>();
        services.TryAddSingleton<IDuplexChannelFactory<StreamingMessage>, FunctionRpcDuplexChannelFactory>();
        services.TryAddSingleton<IRpcClientWorkerChannelFactory, RpcClientWorkerChannelFactory>();
        services.TryAddSingleton<IWorkerChannelRegistry, WorkerChannelRegistry>();

        return services;
    }

    /// <summary>
    /// Adds Client services owned by one ScriptHost child container.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The supplied service collection.</returns>
    internal static IServiceCollection AddRpcClientScriptHostServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IRpcClientFunctionInvocationDispatcher, RpcClientFunctionInvocationDispatcher>();
        services.TryAddSingleton<IFunctionInvocationDispatcherFactory, RpcClientFunctionInvocationDispatcherFactory>();
        services.TryAddSingleton<IWorkerFunctionMetadataProvider, RpcClientWorkerFunctionMetadataProvider>();

        return services;
    }
}
