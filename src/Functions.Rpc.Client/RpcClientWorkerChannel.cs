// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Runs the shared worker protocol over a client-owned FunctionRpc channel.
/// </summary>
/// <remarks>
/// Concurrent and repeated starts share one initialization attempt. A token already canceled when <see cref="StartAsync"/> is called does not
/// begin initialization. Once initialization begins, cancellation only stops the individual caller's wait; shared initialization continues
/// until it succeeds, fails, times out, or the channel is disposed.
/// </remarks>
internal sealed class RpcClientWorkerChannel(string workerId, DuplexChannel<StreamingMessage> ownedChannel, TimeSpan startStreamTimeout,
    IScriptEventManager eventManager, IScriptHostManager hostManager, RpcWorkerConfig workerConfig, ILogger logger, IMetricsLogger metricsLogger,
    int attemptCount, IEnvironment environment, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
    ISharedMemoryManager sharedMemoryManager, IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions,
    IOptions<FunctionsHostingConfigOptions> hostingConfigOptions, IAppCapabilitiesStore appCapabilitiesStore, IHttpProxyService httpProxyService)
    : WorkerChannel(workerId, ownedChannel, eventManager, hostManager, workerConfig, logger, metricsLogger, attemptCount, environment,
        applicationHostOptions, sharedMemoryManager, workerConcurrencyOptions, hostingConfigOptions, appCapabilitiesStore, httpProxyService)
{
    private const int NotStarted = 0;
    private const int Started = 1;
    private const int Disposed = 2;

    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _startStreamTimeout = ValidateStartStreamTimeout(startStreamTimeout);
    private int _lifecycleState;

    /// <summary>
    /// Starts inbound protocol processing and waits for the worker initialization handshake.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels this caller's wait without canceling shared initialization.</param>
    /// <returns>A task that completes after a successful WorkerInitResponse, is canceled for this caller, or faults when initialization cannot complete.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        int state = Interlocked.CompareExchange(ref _lifecycleState, Started, NotStarted);
        ObjectDisposedException.ThrowIf(state == Disposed, this);

        if (state == NotStarted)
        {
            try
            {
                MarkWorkerInitializing();
                BeginInboundProcessing(_startStreamTimeout);
                _ = CompleteStartAsync();
            }
            catch (Exception exception)
            {
                _startCompletion.TrySetException(exception);
            }
        }

        return _startCompletion.Task.WaitAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        Interlocked.Exchange(ref _lifecycleState, Disposed);
        base.Dispose(disposing);
    }

    private async Task CompleteStartAsync()
    {
        try
        {
            if (!await WorkerInitialization.ConfigureAwait(false))
            {
                throw new InvalidOperationException("The worker reported unsuccessful initialization.");
            }

            _startCompletion.TrySetResult();
        }
        catch (OperationCanceledException exception)
        {
            _startCompletion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            _startCompletion.TrySetException(exception);
        }
    }

    private static TimeSpan ValidateStartStreamTimeout(TimeSpan startStreamTimeout)
    {
        if (startStreamTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startStreamTimeout), "The StartStream timeout must be greater than zero.");
        }

        return startStreamTimeout;
    }
}
