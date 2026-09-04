// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Streams HTTP requests and responses between the runtime and worker.
/// </summary>
internal sealed class WorkerHttpForwarder(
    IHttpForwarder httpForwarder, IHttpMessageHandlerFactory httpMessageHandlerFactory)
    : IDisposable
{
    private static readonly ForwarderRequestConfig RequestConfig = new()
    {
        ActivityTimeout = TimeSpan.FromMinutes(4)
    };

    private readonly HttpMessageInvoker _invoker = new(
        httpMessageHandlerFactory.CreateHandler(nameof(WorkerHttpForwarder)), disposeHandler: false);

    /// <summary>
    /// Forwards an HTTP request to the worker destination.
    /// </summary>
    /// <param name="context">The incoming HTTP context.</param>
    /// <param name="destination">The worker destination.</param>
    /// <returns>The YARP forwarding result.</returns>
    public async ValueTask<ForwarderError> ForwardAsync(HttpContext context, Uri destination)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(destination);

        return await httpForwarder.SendAsync(
            context, destination.AbsoluteUri, _invoker, RequestConfig, HttpTransformer.Default);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _invoker.Dispose();
    }
}
