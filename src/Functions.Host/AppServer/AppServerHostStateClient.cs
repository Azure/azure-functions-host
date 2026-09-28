// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Host.AppServer;

/// <summary>
/// Sends Host worker-count snapshots to AppServer's <c>PUT /admin/infra/host/state</c> endpoint.
/// </summary>
/// <remarks>
/// Each request carries an <c>x-ms-site-token</c> JWT signed with <c>WEBSITE_AUTH_ENCRYPTION_KEY</c>. The key is passed
/// explicitly so pod or container key fallbacks never sign AppServer requests. Configuration is read per request because
/// it is reloaded during specialization.
/// </remarks>
internal sealed class AppServerHostStateClient(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IAppServerHostStateClient
{
    internal const string HttpClientName = "AppServerHostState";
    internal const string EndpointConfigurationKey = "FUNCTIONS_APPSERVER_URI";
    internal const string HostStatePath = "admin/infra/host/state";
    internal const string DefaultEndpoint = "http://localhost:6060/";

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    public bool HasSigningKey => !string.IsNullOrEmpty(_configuration[EnvironmentSettingNames.WebSiteAuthEncryptionKey]);

    public async Task<HttpStatusCode> PublishAsync(ComputeRuntimeState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        string? signingKey = _configuration[EnvironmentSettingNames.WebSiteAuthEncryptionKey];
        if (string.IsNullOrEmpty(signingKey))
        {
            throw new InvalidOperationException($"{EnvironmentSettingNames.WebSiteAuthEncryptionKey} is not available.");
        }

        using HttpRequestMessage request = new(HttpMethod.Put, GetHostStateUri());
        request.Headers.Add(ScriptConstants.SiteTokenHeaderName,
            JwtTokenHelper.CreateToken(DateTime.UtcNow.Add(TokenLifetime), key: signingKey.ToKeyBytes()));
        request.Content = JsonContent.Create(
            new HostStateRequest(state.SnapshotVersion, state.LinkedWorkerCount, state.LinkedHttpWorkerCount),
            AppServerJsonSerializerContext.Default.HostStateRequest);

        using HttpResponseMessage response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        return response.StatusCode;
    }

    private Uri GetHostStateUri()
    {
        string? endpoint = _configuration[EndpointConfigurationKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = DefaultEndpoint;
        }

        // A base URI without a trailing slash would otherwise lose its last path segment.
        return new Uri(new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/", UriKind.Absolute), HostStatePath);
    }

    internal sealed record HostStateRequest(long SnapshotVersion, int LinkedWorkerCount, int LinkedHttpWorkerCount);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppServerHostStateClient.HostStateRequest))]
internal sealed partial class AppServerJsonSerializerContext : JsonSerializerContext;
