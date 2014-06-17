namespace Microsoft.Azure.Jobs
{
    // Manage the function index.
    internal interface IFunctionTable : IFunctionTableLookup
    {
        void Add(FunctionDefinition func);
    }
}
