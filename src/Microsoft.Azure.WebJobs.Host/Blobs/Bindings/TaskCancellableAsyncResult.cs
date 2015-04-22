// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal sealed class TaskCancellableAsyncResult : ICancellableAsyncResult, IDisposable
    {
        private readonly Task _task;
        private readonly CancellationTokenSource _cancellationSource;
        private readonly object _state;
        private readonly bool _completedSynchronously;
        private readonly AsyncCallback _callback;

        private bool _cancellationSourceDisposed;
        private bool _disposed;

        public TaskCancellableAsyncResult(Task task, CancellationTokenSource cancellationSource, AsyncCallback callback,
            object state)
        {
            _task = task;
            _cancellationSource = cancellationSource;
            _state = state;
            _completedSynchronously = _task.IsCompleted;

            if (callback != null)
            {
                _callback = callback;

                // Because ContinueWith/ExecuteSynchronously will run immediately for a completed task, ensure this is
                // the last line of the constructor (all other state should be initialized before invoking the
                // callback).
                Task continuation = _task.ContinueWith(InvokeCallback, TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        public object AsyncState
        {
            get
            {
                ThrowIfDisposed();
                return _state;
            }
        }

        public WaitHandle AsyncWaitHandle
        {
            get
            {
                ThrowIfDisposed();
                return ((IAsyncResult)_task).AsyncWaitHandle;
            }
        }

        public bool CompletedSynchronously
        {
            get
            {
                ThrowIfDisposed();
                return _completedSynchronously;
            }
        }

        public bool IsCompleted
        {
            get
            {
                ThrowIfDisposed();
                return _task.IsCompleted;
            }
        }

        public void Cancel()
        {
            ThrowIfDisposed();

            if (_cancellationSourceDisposed)
            {
                throw new InvalidOperationException(
                    "Cannot call ICancellableAsyncResult.Cancel after calling the End method.");
            }

            _cancellationSource.Cancel();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _task.Dispose();

                _disposed = true;
            }
        }

        public void End()
        {
            ThrowIfDisposed();
            _task.GetAwaiter().GetResult();

            if (!_cancellationSourceDisposed)
            {
                _cancellationSource.Dispose();
                _cancellationSourceDisposed = true;
            }
        }

        private void InvokeCallback(Task ignore)
        {
            Debug.Assert(_callback != null);
            _callback.Invoke(this);
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
