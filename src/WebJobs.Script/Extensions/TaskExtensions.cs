// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class WebJobsTaskExtensions
    {
        public static void Ignore(this Task task)
        {
            task.ContinueWith(t =>
            {
                try
                {
                    t.Wait();
                }
                catch (Exception e)
                {
                    System.Diagnostics.Trace.TraceError($"SafeGuard Exception: {e.ToString()}");
                }
            }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
        }

        public static Task IgnoreFailure(this Task task)
        {
            return task.ContinueWith(t =>
            {
                try
                {
                    t.Wait();
                }
                catch (Exception e)
                {
                    System.Diagnostics.Trace.TraceError($"SafeGuard Exception: {e.ToString()}");
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        public static Task<T> IgnoreFailure<T>(this Task<T> task)
        {
            return task.ContinueWith(t =>
            {
                try
                {
                    return t.Result;
                }
                catch (Exception e)
                {
                    System.Diagnostics.Trace.TraceError($"SafeGuard<T> Exception: {e.ToString()}");
                }
                return default(T);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        public static async Task<IEnumerable<T>> IgnoreAndFilterFailures<T>(this IEnumerable<Task<T>> collection)
        {
            return (await collection.Select(t => TaskUtilities.SafeGuardAsync<T>(() => t)).WhenAll()).NotDefaults();
        }

        public static Task WhenAll(this IEnumerable<Task> collection)
        {
            return Task.WhenAll(collection);
        }

        public static Task<T[]> WhenAll<T>(this IEnumerable<Task<T>> collection)
        {
            return Task.WhenAll(collection);
        }
    }
}