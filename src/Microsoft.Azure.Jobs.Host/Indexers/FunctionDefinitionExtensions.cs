using System;

namespace Microsoft.Azure.Jobs
{
    internal static class FunctionDefinitionExtensions
    {
        public static string GetAssemblyFullName(this FunctionDefinition func)
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

            return methodInfoLocation.GetAssemblyFullName();
        }
    }
}
