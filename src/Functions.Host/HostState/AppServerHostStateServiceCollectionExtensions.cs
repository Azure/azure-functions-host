// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Host.HostState;

internal static class AppServerHostStateServiceCollectionExtensions
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddHttpOnDemandWorkerScaleIn(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(
            AppServerHostStateClient.HttpClientName,
            client => client.Timeout = RequestTimeout);
        services.AddSingleton<HostStatePublicationCoordinator>();
        services.AddSingleton<IHostedService, HttpOnDemandWorkerScaleInService>();

        return services;
    }
}
