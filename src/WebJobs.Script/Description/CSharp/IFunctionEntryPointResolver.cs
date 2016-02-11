using System.Collections.Generic;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public interface IFunctionEntryPointResolver
    {
        MethodInfo GetFunctionEntryPoint(IList<MethodInfo> declaredMethods);
    }
}