using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Extensions
{
    public static class TypeExtensions
    {
        public static IEnumerable<Type> GetImplementingTypes(this Type baseType)
        {
            return Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract);
        }
    }
}
