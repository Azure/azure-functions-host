// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class TaskUtilities
    {
        internal static async Task SafeGuardAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.TraceError($"SafeGuard Exception: {e.ToString()}");
            }
        }

        internal static async Task<T> SafeGuardAsync<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.TraceError($"SafeGuard<T> Exception: {e.ToString()}");
                return default(T);
            }
        }

        internal static T SafeGuard<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch
            {
                return default(T);
            }
        }
    }
}