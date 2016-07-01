using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Arm.Extensions
{
    public static class TaskExtensions
    {
        public static void Ignore(this Task task)
        {
            //Empty ignore functions for tasks.
        }

        public static Task IgnoreFailure(this Task task)
        {
            return Utils.SafeGuard(() => task);
        }

        public static Task<T> IgnoreFailure<T>(this Task<T> task)
        {
            return Utils.SafeGuard<T>(() => task);
        }

        public static IEnumerable<Task> IgnoreFailures(this IEnumerable<Task> collection)
        {
            return collection.Select(t => Utils.SafeGuard(() => t));
        }

        public static async Task<IEnumerable<T>> IgnoreAndFilterFailures<T>(this IEnumerable<Task<T>> collection)
        {
            return (await collection.Select(t => Utils.SafeGuard<T>(() => t)).WhenAll()).NotDefaults();
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