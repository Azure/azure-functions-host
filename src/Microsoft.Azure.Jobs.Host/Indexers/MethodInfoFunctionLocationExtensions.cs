using System;

namespace Microsoft.Azure.Jobs
{
    internal static class MethodInfoFunctionLocationExtensions
    {
        public static string GetAssemblyFullName(this MethodInfoFunctionLocation location)
        {
            if (location == null)
            {
                throw new ArgumentNullException("location");
            }

            return GetAssemblyFullName(location.AssemblyQualifiedTypeName);
        }

        // Example input => output pairs:
        // null => null
        // a => null
        // a, => null
        // a,  => null
        // a,b => b
        // a,   b => b
        // MyType, MyAssembly => MyAssembly
        // MyNamespace.MyType, MyAssembly, Culture=neutral, publicKeyToken=null =>
        //   MyAssembly, Culture=neutral, publicKeyToken=null
        private static string GetAssemblyFullName(string assemblyQualifiedTypeName)
        {
            if (assemblyQualifiedTypeName == null)
            {
                return null;
            }

            int index = assemblyQualifiedTypeName.IndexOf(',');

            if (index == -1)
            {
                return null;
            }

            string trimmedName = assemblyQualifiedTypeName.Substring(index + 1).TrimStart(' ');

            if (String.IsNullOrEmpty(trimmedName))
            {
                return null;
            }

            return trimmedName;
        }
    }
}
