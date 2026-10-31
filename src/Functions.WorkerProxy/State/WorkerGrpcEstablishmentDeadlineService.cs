// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Owns the customer worker gRPC establishment deadline from listener readiness through shutdown.
/// </summary>
internal sealed class WorkerGrpcEstablishmentDeadlineService(
    WorkerPodStateManager stateManager,
    FunctionRpcRelay relay,
    IHostApplicationLifetime applicationLifetime,
    ILogger<WorkerGrpcEstablishmentDeadlineService> logger) : IHostedLifecycleService, IDisposable
{
    private readonly CancellationTokenSource _stoppingSource = new();
    private Task _watchTask = Task.CompletedTask;
    private int _started;
    private int _stopping;

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _watchTask = WatchAsync();
            _ = ObserveWatchTaskAsync(_watchTask);
        }

        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        StopWatching();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        StopWatching();
        await _watchTask.WaitAsync(cancellationToken);
    }

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        StopWatching();
        _stoppingSource.Dispose();
    }

    private async Task WatchAsync()
    {
        try
        {
            if (await stateManager.WatchWorkerGrpcEstablishmentAsync(_stoppingSource.Token))
            {
                relay.RequestWorkerRemoval();
            }
        }
        catch (OperationCanceledException) when (_stoppingSource.IsCancellationRequested)
        {
        }
    }

    private async Task ObserveWatchTaskAsync(Task watchTask)
    {
        try
        {
            await watchTask;
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "The Worker gRPC establishment deadline watcher failed.");
            applicationLifetime.StopApplication();
        }
    }

    private void StopWatching()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 0)
        {
            stateManager.OnApplicationStopping();
            _stoppingSource.Cancel();
        }
    }
}
