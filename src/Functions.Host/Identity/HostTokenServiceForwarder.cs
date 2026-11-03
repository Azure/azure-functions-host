// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Host.Identity;

/// <summary>
/// Forwards opaque identity HTTP requests to the app-scoped upstream selected by protected Host configuration.
/// </summary>
/// <remarks>The Host supplies the upstream credential; the worker's local route never selects the destination.</remarks>
internal sealed class HostTokenServiceForwarder(IConfiguration configuration, IHttpClientFactory clients,
    IOptionsMonitor<ScriptApplicationHostOptions> applicationOptions, IScriptWebHostEnvironment environment,
    ILogger<HostTokenServiceForwarder> logger) : ITokenServiceProxyRequestHandler
{
    internal const string ClientName = nameof(HostTokenServiceForwarder);

    /// <inheritdoc />
    public async Task<HttpProxyResponse> SendAsync(HttpProxyRequest request, HttpProxyLimits limits, CancellationToken cancellationToken)
    {
        await WaitForPreparationAsync(cancellationToken);

        // Capture once per outbound request. Routine rotation does not retry or invalidate an already-sent request.
        string? endpointValue = configuration["MSI_ENDPOINT"];
        string? secret = configuration["MSI_SECRET"];
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? endpoint)
            || !(string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Fragment) || !TokenServiceProxyProtocol.IsTokenPath(endpoint.AbsolutePath)
            || string.IsNullOrWhiteSpace(secret) || secret.Any(static character => character is '\r' or '\n' or '\0'))
        {
            logger.LogWarning("Token Service proxy upstream configuration is unavailable or invalid.");
            throw new TokenServiceProxyException(HttpProxyFailure.Types.Code.Unavailable);
        }

        string incomingQuery = TokenServiceProxyProtocol.ParseTarget(request.Target, limits).Query;
        if (!TokenServiceProxyProtocol.IsToken(request.Method)
            || string.Equals(request.Method, "CONNECT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Method, "TRACE", StringComparison.OrdinalIgnoreCase)
            || request.Body.Length > limits.MaxRequestBodyBytes)
        {
            throw new TokenServiceProxyException(HttpProxyFailure.Types.Code.InvalidRequest);
        }

        string query = endpoint.Query;
        if (incomingQuery.Length > 0)
        {
            // Keep both raw query strings, including duplicate keys; the Token Service owns their interpretation.
            query = query.Length == 0 ? incomingQuery : string.Concat(query, "&", incomingQuery[1..]);
        }

        Uri destination = new(string.Concat(endpoint.GetLeftPart(UriPartial.Path), query),
            new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true });
        using HttpRequestMessage outbound = new(new HttpMethod(request.Method), destination)
        {
            Content = new ByteArrayContent(request.Body.ToByteArray())
        };
        foreach (HttpProxyHeader header in TokenServiceProxyProtocol.FilterHeaders(request.Headers, limits))
        {
            if (!outbound.Headers.TryAddWithoutValidation(header.Name, header.Values))
            {
                outbound.Content.Headers.TryAddWithoutValidation(header.Name, header.Values);
            }
        }

        outbound.Headers.Add(TokenServiceProxyProtocol.IdentityHeader, secret);
        try
        {
            using HttpResponseMessage response = await clients.CreateClient(ClientName)
                .SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            HttpProxyResponse result = new() { StatusCode = (uint)response.StatusCode };
            if ((string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase) || (int)response.StatusCode == 304)
                && response.Content.Headers.ContentLength is { } contentLength)
            {
                result.ContentLength = contentLength;
            }

            List<HttpProxyHeader> headers;
            try
            {
                headers = TokenServiceProxyProtocol.FilterHeaders(response.Headers.Concat(response.Content.Headers)
                    .Select(static header => TokenServiceProxyProtocol.Header(header.Key, header.Value)), limits);
            }
            catch (TokenServiceProxyException)
            {
                throw new TokenServiceProxyException(HttpProxyFailure.Types.Code.BadGateway);
            }

            result.Headers.Add(headers);
            result.Body = await TokenServiceProxyProtocol.ReadBodyAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                (int)limits.MaxResponseBodyBytes, response: true, cancellationToken);
            return result;
        }
        catch (HttpRequestException)
        {
            // HttpRequestException can contain the request URI; do not put customer query parameters in logs.
            logger.LogWarning("Token Service proxy could not reach its configured Token Service.");
            throw new TokenServiceProxyException(HttpProxyFailure.Types.Code.BadGateway);
        }
    }

    /// <summary>
    /// Waits for app-scoped Host configuration within the request's existing deadline, without starting a ScriptHost.
    /// </summary>
    private async Task WaitForPreparationAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            // Subscribe before checking state so preparation cannot complete between the check and the subscription.
            using IDisposable? subscription = applicationOptions.OnChange(_ => changed.TrySetResult());
            if (!applicationOptions.CurrentValue.IsStandbyConfiguration)
            {
                await environment.DelayCompletionTask.WaitAsync(cancellationToken);
                return;
            }

            await changed.Task.WaitAsync(cancellationToken);
        }
    }
}
