using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal interface IFunctionIndexLookup
    {
        IFunctionDefinition Lookup(string functionId);

        IFunctionDefinition Lookup(MethodInfo method);
    }
}
