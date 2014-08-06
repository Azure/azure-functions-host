// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq.Language.Flow;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Blobs.Bindings
{
    internal static class ReturnsThrowsOfCloudBlobStreamICancellableAsyncResultExtensions
    {
        public static IReturnsResult<CloudBlobStream> ReturnsCompletedSynchronously(
            this IReturnsThrows<CloudBlobStream, ICancellableAsyncResult> returnsThrows)
        {
            if (returnsThrows == null)
            {
                throw new ArgumentNullException("returnsThrows");
            }

            return returnsThrows.Returns<AsyncCallback, object>((callback, state) =>
            {
                ICancellableAsyncResult result = new CompletedCancellableAsyncResult(state);
                InvokeCallback(callback, result);
                return result;
            });
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
