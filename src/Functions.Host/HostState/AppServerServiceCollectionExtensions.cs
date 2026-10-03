// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Host.HostState;

/// <summary>
/// Registers Host state publication to AppServer.
/// </summary>
internal static class AppServerServiceCollectionExtensions
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Adds the AppServer client and the hosted publisher that sends Host HTTP capacity snapshots.
    /// </summary>
    /// <param name="services">The root service collection to update.</param>
    /// <returns>The supplied service collection.</returns>
    /// <remarks>Requires the compute runtime state manager registered by <c>AddComputeRuntimeStateServices</c>.</remarks>
    public static IServiceCollection AddAppServerHostStatePublisher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(AppServerHostStateClient.HttpClientName, client => client.Timeout = RequestTimeout);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAppServerHostStateClient, AppServerHostStateClient>();
        services.AddSingleton<IHostedService, AppServerHostStatePublisher>();

        return services;
    }
}
