// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Adds platform credentials only to the HTTPS HTTP origin admitted by this worker's Link.
/// </summary>
internal sealed class WorkerProxyHttpProxyService(
    IHttpProxyService inner, RuntimeWorkerCredentials credentials, Uri httpEndpoint) : IHttpProxyService
{
    public Task EnsureSuccessfulForwardingAsync(ScriptInvocationContext context) => inner.EnsureSuccessfulForwardingAsync(context);

    public void StartForwarding(ScriptInvocationContext context, Uri httpUri)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (httpEndpoint is null || !string.Equals(httpEndpoint.AbsoluteUri, httpUri.AbsoluteUri, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HTTP forwarding requires the HTTPS origin supplied by the worker Link.");
        }

        if (!context.TryGetHttpRequest(out HttpRequest request))
        {
            throw new InvalidOperationException("HTTP forwarding requires an HTTP invocation input.");
        }

        IReadOnlyDictionary<string, string> tokens = credentials.CreateHeaders();
        foreach (string name in RuntimeWorkerAuthentication.TokenHeaders)
        {
            request.Headers.Remove(name);
        }

        foreach ((string name, string token) in tokens)
        {
            request.Headers[name] = token;
        }

        inner.StartForwarding(context, httpEndpoint);
    }
}
