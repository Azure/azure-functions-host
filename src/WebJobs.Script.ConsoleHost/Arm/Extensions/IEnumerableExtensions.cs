using System.Collections.Generic;
using System.Linq;

namespace WebJobs.Script.ConsoleHost.Arm.Extensions
{
    public static class IEnumerableExtensions
    {
        public static IEnumerable<T> NotDefaults<T>(this IEnumerable<T> collection)
        {
            return collection.Where(e => !EqualityComparer<T>.Default.Equals(e, default(T)));
        }
    }
}