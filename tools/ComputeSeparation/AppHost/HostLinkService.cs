// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.ComputeSeparation.AppHost;

/// <summary>
/// Performs the platform's calls after the Host, Proxy, and selected worker have started.
/// </summary>
/// <remarks>
/// With a fake platform, the calls go through it in order: assign the pod on the WorkerProxy, then link the worker to
/// the Host. Without one, only the link request is sent, directly to the Host.
/// </remarks>
internal sealed partial class HostLinkService(
    EndpointReference hostEndpoint,
    ReferenceExpression proxyEndpoint,
    EndpointReference? platformEndpoint,
    string workerId,
    string[] dependencies,
    ResourceNotificationService notifications,
    ILogger<HostLinkService> logger) : BackgroundService
{
    private static readonly TimeSpan AssignRetryDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        startup.CancelAfter(TimeSpan.FromMinutes(20));
        try
        {
            await Task.WhenAll(dependencies.Select(name => notifications.WaitForResourceHealthyAsync(name, startup.Token)));

            string hostAddress = await hostEndpoint.GetValueAsync(startup.Token)
                ?? throw new InvalidOperationException("The Host HTTP endpoint was not allocated.");
            if (platformEndpoint is null)
            {
                await LinkDirectlyAsync(hostAddress, startup.Token);
            }
            else
            {
                await AssignAndLinkThroughPlatformAsync(startup.Token);
            }

            Log.WorkerLinked(logger, workerId, new Uri(new Uri(hostAddress), "/api/hello"));
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

    private async Task LinkDirectlyAsync(string hostAddress, CancellationToken cancellationToken)
    {
        string grpcEndpoint = await proxyEndpoint.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException("The WorkerProxy runtime gRPC endpoint was not allocated.");
        using HttpClient client = new() { BaseAddress = new Uri(hostAddress), Timeout = Timeout.InfiniteTimeSpan };
        using HttpResponseMessage response = await client.PutAsJsonAsync($"/admin/workers/{Uri.EscapeDataString(workerId)}", new
        {
            workerGrpcEndpoint = grpcEndpoint,
        }, cancellationToken);

        await EnsureSuccessAsync("The Host rejected", response, cancellationToken);
    }

    private async Task AssignAndLinkThroughPlatformAsync(CancellationToken cancellationToken)
    {
        string platformAddress = await platformEndpoint!.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException("The fake platform HTTP endpoint was not allocated.");
        using HttpClient client = new() { BaseAddress = new Uri(platformAddress), Timeout = Timeout.InfiniteTimeSpan };

        // The WorkerProxy answers 503 until the sample worker has connected, which can trail the worker's start.
        HttpResponseMessage assign = await client.PostAsync("/simulate/worker/assign", null, cancellationToken);
        while (assign.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            assign.Dispose();
            await Task.Delay(AssignRetryDelay, cancellationToken);
            assign = await client.PostAsync("/simulate/worker/assign", null, cancellationToken);
        }

        using (assign)
        {
            await EnsureSuccessAsync("The WorkerProxy rejected the assignment of", assign, cancellationToken);
        }

        Log.WorkerAssigned(logger, workerId);

        using HttpResponseMessage link = await client.PostAsync("/simulate/worker/link", null, cancellationToken);
        await EnsureSuccessAsync("The Host rejected", link, cancellationToken);
    }

    private async Task EnsureSuccessAsync(string failure, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"{failure} worker {workerId}: {(int)response.StatusCode} {body}");
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

        [LoggerMessage(3, LogLevel.Information, "Fake platform assigned the pod for worker {WorkerId} on the WorkerProxy.")]
        public static partial void WorkerAssigned(ILogger logger, string workerId);
    }
}
