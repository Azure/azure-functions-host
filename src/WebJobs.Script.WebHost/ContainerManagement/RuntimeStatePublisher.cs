// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    /// <summary>
    /// Hosted service that forwards <see cref="IRuntimeStateManager"/> state
    /// changes to the mesh service via <see cref="IMeshServiceClient.PublishRuntimeState"/>.
    /// </summary>
    /// <remarks>
    /// Change notifications are debounced over a short window so bursts (e.g.
    /// several <c>OnWorkerAdded</c> calls during initial link, or many
    /// acquire/release operations in quick succession) coalesce into a single
    /// mesh publish carrying the latest snapshot. Only registered when
    /// compute separation is enabled.
    /// </remarks>
    internal sealed class RuntimeStatePublisher : IHostedService, IDisposable
    {
        private const int PublishDebounceMs = 500;

        private readonly IRuntimeStateManager _stateManager;
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly ILogger<RuntimeStatePublisher> _logger;
        private readonly Timer _timer;

        private int _publishPending;
        private int _publishInFlight;
        private bool _disposed;

        public RuntimeStatePublisher(
            IRuntimeStateManager stateManager,
            IMeshServiceClient meshServiceClient,
            ILogger<RuntimeStatePublisher> logger)
        {
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
            _meshServiceClient = meshServiceClient ?? throw new ArgumentNullException(nameof(meshServiceClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _timer = new Timer(OnPublishTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting {name}.", nameof(RuntimeStatePublisher));
            _stateManager.StateChanged += OnStateChanged;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping {name}.", nameof(RuntimeStatePublisher));
            _stateManager.StateChanged -= OnStateChanged;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stateManager.StateChanged -= OnStateChanged;
            _timer.Dispose();
        }

        private void OnStateChanged()
        {
            // First change in a quiet window arms the timer; subsequent changes
            // within the debounce window piggyback on the pending publish and
            // will see the latest snapshot when the timer fires.
            if (Interlocked.CompareExchange(ref _publishPending, 1, 0) == 0)
            {
                try
                {
                    _timer.Change(PublishDebounceMs, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    // Raced with Dispose — drop the publish.
                    Interlocked.Exchange(ref _publishPending, 0);
                }
            }
        }

        private async void OnPublishTimer(object state)
        {
            // Serialize publishes: if a previous invocation is still running
            // (publish slower than the debounce window), let it absorb any
            // newly-pending change via the drain loop below. This prevents
            // concurrent calls into IMeshServiceClient.PublishRuntimeState
            // and avoids out-of-order snapshot delivery.
            if (Interlocked.CompareExchange(ref _publishInFlight, 1, 0) != 0)
            {
                return;
            }

            try
            {
                // Drain the pending flag so changes raised while we were
                // publishing are coalesced into another iteration on this
                // loop rather than racing as a concurrent invocation.
                while (Interlocked.Exchange(ref _publishPending, 0) == 1)
                {
                    try
                    {
                        var snapshot = _stateManager.GetState();
                        await _meshServiceClient.PublishRuntimeState(snapshot);
                    }
                    catch (Exception ex) when (!ex.IsFatal())
                    {
                        _logger.LogError(ex, "Failed to publish runtime state.");
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _publishInFlight, 0);
            }

            // Close the window where a state change armed the timer between
            // our last drain and releasing the gate: that timer callback
            // would have returned early because the gate was held.
            if (Volatile.Read(ref _publishPending) == 1)
            {
                try
                {
                    _timer.Change(0, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    Interlocked.Exchange(ref _publishPending, 0);
                }
            }
        }
    }
}
