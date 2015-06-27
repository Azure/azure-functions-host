// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal sealed class SharedBlobQueueListener : ISharedListener
    {
        private readonly IListener _listener;
        private readonly BlobQueueTriggerExecutor _executor;

        private bool _started;
        private bool _disposed;

        public SharedBlobQueueListener(IListener listener, BlobQueueTriggerExecutor executor)
        {
            _listener = listener;
            _executor = executor;
        }

        public void Register(string functionId, ITriggeredFunctionExecutor executor)
        {
            if (_started)
            {
                throw new InvalidOperationException("Registrations may not be added while the shared listener is running.");
            }

            _executor.Register(functionId, executor);
        }

        public async Task EnsureAllStartedAsync(CancellationToken cancellationToken)
        {
            if (!_started)
            {
                await _listener.StartAsync(cancellationToken);
                _started = true;
            }
        }

        public async Task EnsureAllStoppedAsync(CancellationToken cancellationToken)
        {
            if (_started)
            {
                await _listener.StopAsync(cancellationToken);
                _started = false;
            }
        }

        public void EnsureAllCanceled()
        {
            _listener.Cancel();
        }

        public void EnsureAllDisposed()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _listener.Dispose();
                _disposed = true;
            }
        }
    }
}
