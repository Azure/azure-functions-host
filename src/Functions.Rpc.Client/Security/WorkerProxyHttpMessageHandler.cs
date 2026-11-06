// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Authentication;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Signs only the outbound HTTP request for the selected worker without mutating customer request headers.
/// </summary>
internal sealed class WorkerProxyHttpMessageHandler(RuntimeWorkerCredentials credentials) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is not { IsAbsoluteUri: true } endpoint)
        {
            throw new ArgumentException("WorkerProxy forwarding requires an absolute HTTPS request URI.", nameof(request));
        }

        RpcClientFactory.ValidateEndpoint(new Uri(endpoint.GetLeftPart(UriPartial.Authority)));
        foreach (string name in RuntimeWorkerAuthentication.TokenHeaders)
        {
            request.Headers.Remove(name);
            request.Content?.Headers.Remove(name);
        }

        foreach ((string name, string token) in credentials.CreateHeaders())
        {
            request.Headers.Add(name, token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
