// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
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

internal enum AppServerHostStatePublishResult
{
    Applied,
    CapacityBlocked,
    Unsupported,
    TransientFailure,
    Rejected,
}

internal enum AppServerHostStatePublishMode
{
    Authoritative,
    CapacityFence,
}

/// <summary>
/// Publishes Host capacity through AppServer's existing Host-state transaction.
/// </summary>
internal sealed class AppServerHostStateClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
{
    internal const string CapacityFenceHeader = "x-ms-functions-host-state-capacity-fence";
    internal const string CapacityFenceApplied = "applied";
    internal const string CapacityFenceRequired = "required";
    internal const string DefaultEndpoint = "http://localhost:6060/";
    internal const string HostStatePath = "admin/infra/host/state";
    internal const string HttpClientName = "AppServerHostState";

    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly string _containerName = configuration[EnvironmentSettingNames.ContainerName] ?? string.Empty;
    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    public async Task<AppServerHostStatePublishResult> PublishAsync(
        HostStateSnapshot state,
        AppServerHostStatePublishMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(_containerName))
        {
            return AppServerHostStatePublishResult.Unsupported;
        }

        using HttpRequestMessage request = new(HttpMethod.Put, GetHostStateUri())
        {
            Content = JsonContent.Create(
                new HostStateRequest(state.CreatedTime, state.LinkedWorkerCount, state.HttpCapacity),
                AppServerHostStateJsonContext.Default.HostStateRequest),
        };
        request.Headers.Add(ScriptConstants.ContainerInstanceHeader, _containerName);
        if (mode is AppServerHostStatePublishMode.CapacityFence)
        {
            request.Headers.Add(CapacityFenceHeader, CapacityFenceRequired);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(
                request,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AppServerHostStatePublishResult.TransientFailure;
        }
        catch (HttpRequestException)
        {
            return AppServerHostStatePublishResult.TransientFailure;
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.PreconditionFailed)
            {
                return AppServerHostStatePublishResult.CapacityBlocked;
            }

            if (response.IsSuccessStatusCode)
            {
                if (mode is AppServerHostStatePublishMode.Authoritative)
                {
                    return AppServerHostStatePublishResult.Applied;
                }

                return response.Headers.TryGetValues(CapacityFenceHeader, out var values) &&
                    values.Any(value => string.Equals(value, CapacityFenceApplied, StringComparison.OrdinalIgnoreCase))
                        ? AppServerHostStatePublishResult.Applied
                        : AppServerHostStatePublishResult.Unsupported;
            }

            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
                (int)response.StatusCode >= 500
                    ? AppServerHostStatePublishResult.TransientFailure
                    : AppServerHostStatePublishResult.Rejected;
        }
    }

    private Uri GetHostStateUri()
    {
        string endpoint = _configuration[EnvironmentSettingNames.MeshInitURI] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = DefaultEndpoint;
        }

        return new Uri(new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/", UriKind.Absolute), HostStatePath);
    }

    internal sealed record HostStateRequest(
        DateTimeOffset CreatedTime,
        int LinkedWorkerCount,
        long HttpCapacity);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppServerHostStateClient.HostStateRequest))]
internal sealed partial class AppServerHostStateJsonContext : JsonSerializerContext;
