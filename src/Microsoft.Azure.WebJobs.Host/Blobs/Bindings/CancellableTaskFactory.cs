// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal static class CancellableTaskFactory
    {
        // Similar to TaskFactory.FromAsync, except it supports cancellation using ICancellableAsyncResult.
        public static Task FromAsync(Func<AsyncCallback, object, ICancellableAsyncResult> beginMethod,
            Action<IAsyncResult> endMethod, CancellationToken cancellationToken)
        {
            TaskCompletionSource<object> source = new TaskCompletionSource<object>();

            CancellationTokenRegistration cancellationRegistration = new CancellationTokenRegistration();
            bool cancellationRegistrationDisposed = false;
            object cancellationRegistrationLock = new object();

            ICancellableAsyncResult result = beginMethod.Invoke((ar) =>
            {
                lock (cancellationRegistrationLock)
                {
                    cancellationRegistration.Dispose();
                    cancellationRegistrationDisposed = true;
                }

                try
                {
                    endMethod.Invoke(ar);
                    source.SetResult(null);
                }
                catch (OperationCanceledException)
                {
                    source.SetCanceled();
                }
                catch (Exception exception)
                {
                    source.SetException(exception);
                }
            }, null);

            lock (cancellationRegistrationLock)
            {
                if (!cancellationRegistrationDisposed)
                {
                    cancellationRegistration = cancellationToken.Register(result.Cancel);
                }
            }

            if (result.CompletedSynchronously)
            {
                System.Diagnostics.Debug.Assert(source.Task.IsCompleted);
            }

            return source.Task;
        }
    }
}
