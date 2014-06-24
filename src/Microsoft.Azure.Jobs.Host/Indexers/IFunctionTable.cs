namespace Microsoft.Azure.Jobs.Host.Indexers
{
    // Manage the function index.
    internal interface IFunctionTable : IFunctionTableLookup
    {
        void Add(FunctionDefinition func);
    }
}
