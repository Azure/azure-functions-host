using System;
using System.Globalization;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal static class MethodInfoExtensions
    {
        public static string GetFullName(this MethodInfo methodInfo)
        {
            if (methodInfo == null)
            {
                throw new ArgumentNullException("methodInfo");
            }

            return String.Format(CultureInfo.InvariantCulture, "{0}.{1}", methodInfo.DeclaringType.FullName, methodInfo.Name);
        }

        public static string GetShortName(this MethodInfo methodInfo)
        {
            if (methodInfo == null)
            {
                throw new ArgumentNullException("methodInfo");
            }

            return String.Format(CultureInfo.InvariantCulture, "{0}.{1}", methodInfo.DeclaringType.Name, methodInfo.Name);
        }
    }
}
