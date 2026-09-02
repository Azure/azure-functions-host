// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Provides WorkerProxy listener-role checks for HTTP requests.
/// </summary>
public static class HttpContextExtensions
{
    extension(HttpContext context)
    {
        /// <summary>
        /// Gets a value indicating whether the request arrived on the management listener.
        /// </summary>
        /// <returns><see langword="true"/> if the request arrived on the management listener; otherwise, <see langword="false"/>.</returns>
        public bool IsManagementPort()
        {
            return context.GetProxyEndpoints().IsManagementPort(context.Connection.LocalPort);
        }

        /// <summary>
        /// Gets a value indicating whether the request arrived on either FunctionRpc listener.
        /// </summary>
        /// <returns><see langword="true"/> if the request arrived on a FunctionRpc listener; otherwise, <see langword="false"/>.</returns>
        public bool IsAnyGrpcPort()
        {
            return context.GetProxyEndpoints().TryGetRelaySide(context.Connection.LocalPort, out _);
        }

        /// <summary>
        /// Gets a value indicating whether the request arrived on the HTTP forwarding listener.
        /// </summary>
        /// <returns><see langword="true"/> if the request arrived on the HTTP forwarding listener; otherwise, <see langword="false"/>.</returns>
        public bool IsHttpPort()
        {
            return context.GetProxyEndpoints().IsHttpPort(context.Connection.LocalPort);
        }

        private WorkerProxyEndpointConfiguration GetProxyEndpoints()
        {
            return context.RequestServices.GetRequiredService<WorkerProxyEndpointConfiguration>();
        }
    }
}