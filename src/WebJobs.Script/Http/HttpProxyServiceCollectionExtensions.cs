// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering the shared HTTP invocation proxy.
/// </summary>
public static class HttpProxyServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default HTTP invocation proxy and its forwarding dependency.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddDefaultHttpProxyService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpForwarder();
        services.AddSingleton<IHttpProxyService, DefaultHttpProxyService>();

        return services;
    }
}
