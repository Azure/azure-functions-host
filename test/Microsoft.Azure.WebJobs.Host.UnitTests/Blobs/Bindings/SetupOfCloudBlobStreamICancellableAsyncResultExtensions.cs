// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq.Language.Flow;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Blobs.Bindings
{
    internal static class SetupOfCloudBlobStreamICancellableAsyncResultExtensions
    {
        public static IReturnsResult<CloudBlobStream> ReturnsCompletedSynchronously(
            this ISetup<CloudBlobStream, ICancellableAsyncResult> setup)
        {
            if (setup == null)
            {
                throw new ArgumentNullException("setup");
            }

            return setup.Returns<AsyncCallback, object>((callback, state) =>
            {
                ICancellableAsyncResult result = new CompletedCancellableAsyncResult(state);
                InvokeCallback(callback, result);
                return result;
            });
        }

        public static IReturnsResult<CloudBlobStream> ReturnsCompletedSynchronously(
            this ISetup<CloudBlobStream, ICancellableAsyncResult> setup, CompletedCancellationSpy spy)
        {
            if (setup == null)
            {
                throw new ArgumentNullException("setup");
            }

            return setup.Returns<AsyncCallback, object>((callback, state) =>
            {
                CompletedCancellableAsyncResult result = new CompletedCancellableAsyncResult(state);
                spy.SetAsyncResult(result);
                InvokeCallback(callback, result);
                return result;
            });
        }

        public static IReturnsResult<CloudBlobStream> ReturnsCompletingAsynchronously(
            this ISetup<CloudBlobStream, ICancellableAsyncResult> setup,
            CancellableAsyncCompletionSource completionSource)
        {
            if (setup == null)
            {
                throw new ArgumentNullException("setup");
            }

            return setup.Returns<AsyncCallback, object>((callback, state) =>
            {
                CompletingCancellableAsyncResult result = new CompletingCancellableAsyncResult(callback, state);
                completionSource.SetAsyncResult(result);
                return result;
            });
        }

        public static IReturnsResult<CloudBlobStream> ReturnsUncompleted(
            this ISetup<CloudBlobStream, ICancellableAsyncResult> setup)
        {
            if (setup == null)
            {
                throw new ArgumentNullException("setup");
            }

            return setup.Returns<AsyncCallback, object>((i4, state) => new UncompletedCancellableAsyncResult(state));
        }

        private static void InvokeCallback(AsyncCallback callback, IAsyncResult result)
        {
            if (callback != null)
            {
                callback.Invoke(result);
            }
        }
    }
}
