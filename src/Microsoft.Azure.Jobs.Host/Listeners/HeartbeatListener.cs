// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Listeners
{
    internal class HeartbeatListener : IListener
    {
        private readonly ICanFailCommand _heartbeatCommand;
        private readonly IListener _innerListener;
        private readonly IntervalSeparationTimer _timer;

        private bool _disposed;

        public HeartbeatListener(ICanFailCommand heartbeatCommand, IListener innerListener)
        {
            _heartbeatCommand = heartbeatCommand;
            _innerListener = innerListener;
            _timer = CreateTimer();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _innerListener.StartAsync(cancellationToken);

            await _heartbeatCommand.TryExecuteAsync(cancellationToken);
            _timer.Start();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer.Stop();

            return _innerListener.StopAsync(cancellationToken);
        }

        public void Cancel()
        {
            _timer.Cancel();
            _innerListener.Cancel();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer.Dispose();
                _innerListener.Dispose();

                _disposed = true;
            }
        }

        private IntervalSeparationTimer CreateTimer()
        {
            return LinearSpeedupTimerCommand.CreateTimer(_heartbeatCommand,
                HeartbeatIntervals.NormalSignalInterval, HeartbeatIntervals.MinimumSignalInterval);
        }
    }
}
