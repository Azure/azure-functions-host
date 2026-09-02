// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Provides WorkerProxy listener-role checks for HTTP requests.
/// </summary>
internal static class HttpContextExtensions
{
    /// <summary>
    /// Determines whether the request arrived on the management listener.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns><see langword="true"/> for the management listener; otherwise, <see langword="false"/>.</returns>
    public static bool IsManagementPort(this HttpContext context)
    {
        return context.GetProxyEndpoints().IsManagementPort(context.Connection.LocalPort);
    }

    /// <summary>
    /// Determines whether the request arrived on either FunctionRpc listener.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns><see langword="true"/> for a FunctionRpc listener; otherwise, <see langword="false"/>.</returns>
    public static bool IsAnyGrpcPort(this HttpContext context)
    {
        return context.GetProxyEndpoints().TryGetRelaySide(context.Connection.LocalPort, out _);
    }

    private static WorkerProxyEndpointConfiguration GetProxyEndpoints(this HttpContext context)
    {
        return context.RequestServices.GetRequiredService<WorkerProxyEndpointConfiguration>();
    }
}
