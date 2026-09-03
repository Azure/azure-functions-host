// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Resolves the worker endpoint, waits for readiness, and forwards eligible requests through YARP.
/// </summary>
internal sealed class WorkerHttpForwardingMiddleware(
    IOptions<WorkerProxyOptions> options,
    WorkerEndpointReadinessProbe readinessProbe,
    WorkerHttpForwarder forwarder)
{
    /// <summary>
    /// Forwards a request to the configured worker HTTP endpoint.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // TODO: Supply the worker-advertised HTTP endpoint once FunctionRpc exposes it.
        Uri? destination = WorkerHttpDestinationResolver.Resolve(
            options.Value.WorkerHttpEndpoint, advertisedEndpoint: null);

        if (destination is null)
        {
            WorkerHttpForwardingTelemetry.RecordDestinationNotConfigured(context);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (!readinessProbe.IsKnownReady(destination)
            && !await readinessProbe.WaitForReadyAsync(destination, context.RequestAborted))
        {
            WorkerHttpForwardingTelemetry.RecordDestinationNotReady(context);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        ForwarderError error = await forwarder.ForwardAsync(context, destination);
        if (error is ForwarderError.None)
        {
            WorkerHttpForwardingTelemetry.RecordSuccess(context);
            return;
        }

        if (context.RequestAborted.IsCancellationRequested)
        {
            WorkerHttpForwardingTelemetry.RecordCanceled(context);
            return;
        }

        WorkerHttpForwardingTelemetry.RecordForwarderError(context, error);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }
}
