// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Listeners
{
    internal sealed class CompositeListener : IListener
    {
        private readonly IEnumerable<IListener> _listeners;

        private bool _disposed;

        public CompositeListener(IEnumerable<IListener> listeners)
        {
            _listeners = listeners;
        }

        public void Start()
        {
            ThrowIfDisposed();

            foreach (IListener listener in _listeners)
            {
                listener.Start();
            }
        }

        public void Stop()
        {
            ThrowIfDisposed();

            foreach (IListener listener in _listeners)
            {
                listener.Stop();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (IListener listener in _listeners)
                {
                    listener.Dispose();
                }

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
