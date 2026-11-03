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
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Host.HostState;

/// <summary>
/// Sends snapshots to the prototype AppServer <c>PUT /admin/infra/host/state</c> endpoint.
/// </summary>
/// <remarks>
/// Requests are not authenticated. The endpoint is resolved on the first publication and reused for the client's lifetime.
/// </remarks>
internal sealed class AppServerHostStateClient(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IAppServerHostStateClient
{
    internal const string HttpClientName = "AppServerHostState";
    internal const string HostStatePath = "admin/infra/host/state";
    internal const string DefaultEndpoint = "http://localhost:6060/";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private Uri? _hostStateUri;

    public async Task<HttpStatusCode> PublishAsync(RuntimeState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        using HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            _hostStateUri ??= GetHostStateUri(),
            new HostStateRequest(state.CreatedTime, state.HttpCapacity, LinkedWorkerCount: state.WorkerCount),
            AppServerJsonSerializerContext.Default.HostStateRequest,
            cancellationToken);

        return response.StatusCode;
    }

    private Uri GetHostStateUri()
    {
        string? endpoint = _configuration[EnvironmentSettingNames.MeshInitURI];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = DefaultEndpoint;
        }

        return new Uri($"{endpoint.TrimEnd('/')}/{HostStatePath}", UriKind.Absolute);
    }

    internal sealed record HostStateRequest(DateTimeOffset CreatedTime, long HttpCapacity, int LinkedWorkerCount);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppServerHostStateClient.HostStateRequest))]
internal sealed partial class AppServerJsonSerializerContext : JsonSerializerContext;
