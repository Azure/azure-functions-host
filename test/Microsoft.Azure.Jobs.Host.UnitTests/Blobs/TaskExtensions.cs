// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Blobs
{
    internal static class TaskExtensions
    {
        public static void WaitUntilCompleted(this Task task, int millisecondsTimeout)
        {
            if (task == null)
            {
                throw new ArgumentNullException("task");
            }

            task.ContinueWith((t) =>
            {
                if (t.IsFaulted)
                {
                    // Observe the exception in the faulted case to avoid an unobserved exception leaking and killing
                    // the thread finalizer.
                    var observed = t.Exception;
                }
            }, TaskContinuationOptions.ExecuteSynchronously).Wait(millisecondsTimeout);
        }
    }
}
