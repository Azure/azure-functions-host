
namespace RunnerInterfaces
{
    // Manage the function index.
    internal interface IFunctionTable : IFunctionTableLookup
    {
        void Add(FunctionDefinition func);
        void Delete(FunctionDefinition func);
    }

    internal interface IFunctionTableLookup
    {
        // Function Id is the location.ToString().
        FunctionDefinition Lookup(string functionId);
        FunctionDefinition[] ReadAll();
    }
    internal static class IFunctionTableLookupExtensions
    {
        public static FunctionDefinition Lookup(this IFunctionTableLookup lookup, FunctionLocation location)
        {
            string rowKey = location.ToString();
            return lookup.Lookup(rowKey);
        }
    }
}