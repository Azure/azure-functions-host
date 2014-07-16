// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Listeners;

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    internal sealed class BlobListener : IListener
    {
        private readonly SharedBlobListener _sharedListener;

        private bool _started;
        private bool _disposed;

        public BlobListener(SharedBlobListener sharedListener)
        {
            _sharedListener = sharedListener;
        }

        public void Start()
        {
            ThrowIfDisposed();

            if (_started)
            {
                throw new InvalidOperationException("The listener has already been started.");
            }

            // Starts the entire shared listener (if not yet started).
            // There is currently no scenario for controlling a single blob listener independently.
            _sharedListener.EnsureAllStarted();
            _started = true;
        }

        public void Stop()
        {
            ThrowIfDisposed();

            if (!_started)
            {
                throw new InvalidOperationException(
                    "The listener has not yet been started or has already been stopped.");
            }

            // Stops the entire shared listener (if not yet stopped).
            // There is currently no scenario for controlling a single blob listener independently.
            _sharedListener.EnsureAllStopped();
            _started = false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Disposes the entire shared listener (if not yet disposed).
                // There is currently no scenario for controlling a single blob listener independently.
                _sharedListener.EnsureAllDisposed();

                _disposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }
    }
}
