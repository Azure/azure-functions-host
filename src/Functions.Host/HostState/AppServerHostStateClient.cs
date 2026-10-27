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
/// Sends Host HTTP capacity and linked worker count snapshots to AppServer's <c>PUT /admin/infra/host/state</c> endpoint.
/// </summary>
/// <remarks>
/// The endpoint is read per request because it is reloaded during specialization. The platform container identity is captured at startup.
/// </remarks>
internal sealed class AppServerHostStateClient(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IAppServerHostStateClient
{
    internal const string HttpClientName = "AppServerHostState";
    internal const string HostStatePath = "admin/infra/host/state";
    internal const string DefaultEndpoint = "http://localhost:6060/";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly string _containerName = GetContainerName(configuration ?? throw new ArgumentNullException(nameof(configuration)));

    public async Task<HttpStatusCode> PublishAsync(ComputeRuntimeState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        using HttpRequestMessage request = new(HttpMethod.Put, GetHostStateUri())
        {
            Content = JsonContent.Create(
                new HostStateRequest(state.CreatedTime, state.HttpCapacity, LinkedWorkerCount: state.WorkerCount),
                AppServerJsonSerializerContext.Default.HostStateRequest),
        };
        request.Headers.Add(ScriptConstants.ContainerInstanceHeader, _containerName);

        using HttpResponseMessage response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(
            request,
            cancellationToken);

        return response.StatusCode;
    }

    private static string GetContainerName(IConfiguration configuration)
    {
        string? containerName = configuration[EnvironmentSettingNames.ContainerName];
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new InvalidOperationException($"{EnvironmentSettingNames.ContainerName} must be configured before publishing Host state.");
        }

        return containerName;
    }

    private Uri GetHostStateUri()
    {
        string? endpoint = _configuration[EnvironmentSettingNames.MeshInitURI];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = DefaultEndpoint;
        }

        // A base URI without a trailing slash would otherwise lose its last path segment.
        return new Uri(new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/", UriKind.Absolute), HostStatePath);
    }

    internal sealed record HostStateRequest(DateTimeOffset CreatedTime, long HttpCapacity, int LinkedWorkerCount);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppServerHostStateClient.HostStateRequest))]
internal sealed partial class AppServerJsonSerializerContext : JsonSerializerContext;
