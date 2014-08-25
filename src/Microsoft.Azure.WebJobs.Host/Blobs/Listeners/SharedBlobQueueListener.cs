// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal sealed class SharedBlobQueueListener : ISharedListener
    {
        private readonly ITaskSeriesTimer _timer;
        private readonly BlobQueueTriggerExecutor _executor;

        private bool _started;
        private bool _disposed;

        public SharedBlobQueueListener(ITaskSeriesTimer timer, BlobQueueTriggerExecutor executor)
        {
            _timer = timer;
            _executor = executor;
        }

        public void Register(string functionId, ITriggeredFunctionInstanceFactory<ICloudBlob> instanceFactory)
        {
            if (_started)
            {
                throw new InvalidOperationException(
                    "Registrations may not be added while the shared listener is running.");
            }

            _executor.Register(functionId, instanceFactory);
        }

        public void EnsureAllStarted()
        {
            if (!_started)
            {
                _timer.Start();
                _started = true;
            }
        }

        public async Task EnsureAllStopped(CancellationToken cancellationToken)
        {
            if (_started)
            {
                await _timer.StopAsync(cancellationToken);
                _started = false;
            }
        }

        public void EnsureAllCanceled()
        {
            _timer.Cancel();
        }

        public void EnsureAllDisposed()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer.Dispose();
                _disposed = true;
            }
        }
    }
}
