// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class TaskAsyncResult<TResult> : IAsyncResult
    {
        private readonly Task<TResult> _task;
        private readonly object _state;
        private readonly bool _completedSynchronously;

        public TaskAsyncResult(Task<TResult> task, AsyncCallback callback, object state)
        {
            if (callback != null)
            {
                _task = AddCallback(task, callback);
            }
            else
            {
                _task = task;
            }

            _state = state;
            _completedSynchronously = _task.IsCompleted;
        }

        public object AsyncState
        {
            get { return _state; }
        }

        public WaitHandle AsyncWaitHandle
        {
            get { return ((IAsyncResult)_task).AsyncWaitHandle; }
        }

        public bool CompletedSynchronously
        {
            get { return _completedSynchronously; }
        }

        public bool IsCompleted
        {
            get { return _task.IsCompleted; }
        }

        public TResult End()
        {
            return _task.GetAwaiter().GetResult();
        }

        private async Task<TResult> AddCallback(Task<TResult> task, AsyncCallback callback)
        {
            Debug.Assert(callback != null);

            try
            {
                return await task;
            }
            finally
            {
                callback.Invoke(this);
            }
        }
    }
}
