// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.ComputeSeparation.AppHost;

/// <summary>
/// Performs the platform's link request after the Host, Proxy, and selected worker have started.
/// </summary>
internal sealed partial class HostLinkService(
    EndpointReference hostEndpoint,
    ReferenceExpression proxyEndpoint,
    string workerId,
    string[] dependencies,
    ResourceNotificationService notifications,
    ILogger<HostLinkService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        startup.CancelAfter(TimeSpan.FromMinutes(20));
        try
        {
            await Task.WhenAll(dependencies.Select(name => notifications.WaitForResourceHealthyAsync(name, startup.Token)));

            string address = await hostEndpoint.GetValueAsync(startup.Token)
                ?? throw new InvalidOperationException("The Host HTTP endpoint was not allocated.");
            string grpcEndpoint = await proxyEndpoint.GetValueAsync(startup.Token)
                ?? throw new InvalidOperationException("The WorkerProxy runtime gRPC endpoint was not allocated.");
            using HttpClient client = new() { BaseAddress = new Uri(address), Timeout = Timeout.InfiniteTimeSpan };
            using HttpResponseMessage response = await client.PutAsJsonAsync("/admin/workers", new
            {
                workerPodName = workerId,
                workerGrpcEndpoint = grpcEndpoint
            }, startup.Token);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(startup.Token);
                throw new HttpRequestException($"The Host rejected worker {workerId}: {(int)response.StatusCode} {body}");
            }

            Log.WorkerLinked(logger, workerId, new Uri(client.BaseAddress, "/api/hello"));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException exception)
        {
            Log.WorkerLinkTimedOut(logger, workerId, exception);
        }
        catch (HttpRequestException exception)
        {
            Log.WorkerLinkFailed(logger, workerId, exception);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Host linked worker {WorkerId}. The function will be available at {FunctionEndpoint} after indexing.")]
        public static partial void WorkerLinked(ILogger logger, string workerId, Uri functionEndpoint);

        [LoggerMessage(1, LogLevel.Error, "Timed out waiting for resources or linking worker {WorkerId}. Resources remain running for diagnosis.")]
        public static partial void WorkerLinkTimedOut(ILogger logger, string workerId, Exception exception);

        [LoggerMessage(2, LogLevel.Error, "Failed to link worker {WorkerId}. Resources remain running for diagnosis.")]
        public static partial void WorkerLinkFailed(ILogger logger, string workerId, Exception exception);
    }
}
