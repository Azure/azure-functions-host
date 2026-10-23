// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Host.HostState;

/// <summary>
/// Registers root-owned Host state publication to AppServer.
/// </summary>
internal static class AppServerServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the AppServer client and the hosted publisher to the root service collection.
        /// </summary>
        /// <returns>The supplied service collection.</returns>
        /// <remarks>Requires the runtime state manager registered by <c>AddRpcClientWebHostServices</c>.</remarks>
        public IServiceCollection AddAppServerHostStatePublisher()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddHttpClient(AppServerHostStateClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IAppServerHostStateClient, AppServerHostStateClient>();
            services.AddSingleton<IHostedService, AppServerHostStatePublisher>();

            return services;
        }
    }
}
