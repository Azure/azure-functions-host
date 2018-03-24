// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class IEnumerableTasksExtensions
    {
        /// <summary>
        /// Returns a Task that completes when all the tasks in <paramref name="collection"/> have completed.
        /// </summary>
        /// <param name="collection">tasks to be reduced.</param>
        /// <returns>Task that completes when all tasks are done.</returns>
        public static Task WhenAll(this IEnumerable<Task> collection)
        {
            return Task.WhenAll(collection);
        }

        /// <summary>
        /// Returns a Task that completes when all the tasks in <paramref name="collection"/> have completed.
        /// </summary>
        /// <param name="collection">tasks to be reduced.</param>
        /// <returns>Task that completes when all tasks are done.</returns>
        public static Task<T[]> WhenAll<T>(this IEnumerable<Task<T>> collection)
        {
            return Task.WhenAll(collection);
        }
    }
}