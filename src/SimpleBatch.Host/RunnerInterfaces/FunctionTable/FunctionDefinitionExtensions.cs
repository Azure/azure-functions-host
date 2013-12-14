using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class FunctionDefinitionExtensions
    {
        internal static string GetAssemblyFullName(this FunctionDefinition func)
        {
            if (func == null)
            {
                throw new ArgumentNullException("func");
            }

            var methodInfoLocation = func.Location as MethodInfoFunctionLocation;

            if (methodInfoLocation == null)
            {
                return null;
            }

            return GetAssemblyFullName(methodInfoLocation.AssemblyQualifiedTypeName);
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
