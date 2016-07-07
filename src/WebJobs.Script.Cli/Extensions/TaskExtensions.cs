// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebJobs.Script.Cli.Extensions
{
    internal static class TaskExtensions
    {
        public static void Ignore(this Task task)
        {
            //Empty ignore functions for tasks.
        }

        public static Task IgnoreFailure(this Task task)
        {
            return Utilities.SafeguardAsync(() => task);
        }

        public static Task<T> IgnoreFailure<T>(this Task<T> task)
        {
            return Utilities.SafeguardAsync<T>(() => task);
        }

        public static async Task<IEnumerable<T>> IgnoreAndFilterFailures<T>(this IEnumerable<Task<T>> collection)
        {
            return (await collection.Select(t => Utilities.SafeguardAsync<T>(() => t)).WhenAll()).NotDefaults();
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