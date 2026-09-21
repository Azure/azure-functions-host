// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Owns first-link activation and shutdown while borrowing the root ScriptHost service and worker registry.
/// </summary>
/// <param name="channelRegistry">The root-owned worker channel registry.</param>
/// <param name="scriptHost">The root-owned ScriptHost service, supplied without an automatic hosted-service registration.</param>
/// <param name="applicationLifetime">The root Host's application lifetime.</param>
/// <param name="logger">The lifecycle logger.</param>
internal sealed partial class RpcClientScriptHostStartupCoordinator(
    IWorkerChannelRegistry channelRegistry,
    IHostedService scriptHost,
    IHostApplicationLifetime applicationLifetime,
    ILogger<RpcClientScriptHostStartupCoordinator> logger) : IHostedService, IAsyncDisposable
{
    private readonly IWorkerChannelRegistry _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
    private readonly IHostedService _scriptHost = scriptHost ?? throw new ArgumentNullException(nameof(scriptHost));
    private readonly ILogger<RpcClientScriptHostStartupCoordinator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly CancellationTokenSource _shutdown =
        CancellationTokenSource.CreateLinkedTokenSource((applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime))).ApplicationStopping);

    private readonly Lock _lifecycleLock = new();
    private readonly Lock _disposeLock = new();
    private Task? _activationTask;
    private Task? _stopTask;
    private Task? _disposeTask;

    /// <summary>
    /// Gets the first-link activation task, or <see langword="null"/> before this hosted service starts.
    /// </summary>
    /// <remarks>
    /// Completion means the initial call to <see cref="IHostedService.StartAsync"/> has returned, not that ScriptHost is running.
    /// The manager owns retries and exposes their outcome through <see cref="IScriptHostManager.State"/> and
    /// <see cref="IScriptHostManager.LastError"/>. Later links do not replace this task.
    /// </remarks>
    public Task? ActivationTask
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _activationTask;
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lifecycleLock)
        {
            if (_stopTask is not null)
            {
                throw new InvalidOperationException("Client ScriptHost startup has already stopped.");
            }

            _activationTask ??= ActivateScriptHostAsync();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task stopTask;
        lock (_lifecycleLock)
        {
            stopTask = _stopTask ??= StopCoreAsync();
        }

        // Cancel only this caller's wait; cleanup remains owned by the coordinator.
        return stopTask.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            return new(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task ActivateScriptHostAsync()
    {
        // Even a preexisting link must not synchronously block root hosted-service startup.
        await Task.Yield();
        CancellationToken cancellationToken = _shutdown.Token;

        try
        {
            await _channelRegistry.WaitForFirstInitializedAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Log.LinkObserved(_logger);

            Log.StartTriggered(_logger);
            await _scriptHost.StartAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Log.StartCallCompleted(_logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.StartCanceled(_logger);
            throw;
        }
        catch (Exception exception)
        {
            Log.ActivationFailed(_logger, exception);
            throw;
        }
    }

    private async Task StopCoreAsync()
    {
        // Cancellation callbacks and child shutdown must run outside the lifecycle lock.
        await Task.Yield();
        Exception? cleanupException = null;
        Exception? activationException = null;
        try
        {
            await _shutdown.CancelAsync();
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        Task? activationTask = ActivationTask;
        if (activationTask is not null)
        {
            try
            {
                await activationTask;
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                // This failure is already logged and remains observable through ActivationTask.
                activationException = exception;
            }
        }

        try
        {
            await _scriptHost.StopAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            cleanupException = AggregateException.Combine(cleanupException, exception);
        }

        if (cleanupException is not null)
        {
            cleanupException = AggregateException.Combine(activationException, cleanupException);
            Log.StopFailed(_logger, cleanupException);
            ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task stopTask = StopAsync(CancellationToken.None);
        try
        {
            await stopTask;
        }
        catch (Exception) when (stopTask.IsFaulted || stopTask.IsCanceled)
        {
            // StopAsync retains and logs the failure. Rethrowing here would prevent the root container from disposing
            // its remaining services, including the registry that owns the worker channels.
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "The first initialized worker link was observed.")]
        public static partial void LinkObserved(ILogger logger);

        [LoggerMessage(1, LogLevel.Information, "Starting ScriptHost after the first initialized worker link.")]
        public static partial void StartTriggered(ILogger logger);

        [LoggerMessage(2, LogLevel.Information, "The initial ScriptHost startup call completed. ScriptHost manages any further startup retries.")]
        public static partial void StartCallCompleted(ILogger logger);

        [LoggerMessage(3, LogLevel.Error, "First-link ScriptHost activation failed. The worker registry remains available.")]
        public static partial void ActivationFailed(ILogger logger, Exception exception);

        [LoggerMessage(4, LogLevel.Information, "Deferred ScriptHost startup was canceled during shutdown.")]
        public static partial void StartCanceled(ILogger logger);

        [LoggerMessage(5, LogLevel.Error, "Client ScriptHost shutdown failed.")]
        public static partial void StopFailed(ILogger logger, Exception exception);
    }
}
