// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Restricts worker HTTP forwarding to the dedicated forwarding listener.
/// </summary>
internal sealed class WorkerHttpForwardingMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Forwards requests arriving on the HTTP forwarding listener.
    /// </summary>
    public async Task InvokeAsync(
        HttpContext context,
        WorkerProxyEndpointConfiguration endpoints,
        IOptions<WorkerProxyOptions> options,
        WorkerEndpointReadinessProbe readinessProbe,
        WorkerHttpForwarder forwarder)
    {
        if (!endpoints.IsHttpForwardingPort(context.Connection.LocalPort))
        {
            await next(context);
            return;
        }

        System.Uri? destination = WorkerHttpDestinationResolver.Resolve(
            options.Value.WorkerHttpEndpoint, advertisedEndpoint: null);

        if (destination is null || (!readinessProbe.IsKnownReady(destination)
            && !await readinessProbe.WaitForReadyAsync(destination, context.RequestAborted)))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        ForwarderError error = await forwarder.ForwardAsync(context, destination);
        if (error is not ForwarderError.None && !context.RequestAborted.IsCancellationRequested
            && !context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }
}
