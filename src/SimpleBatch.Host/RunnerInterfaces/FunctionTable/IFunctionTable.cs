namespace Microsoft.WindowsAzure.Jobs
{
    // Manage the function index.
    internal interface IFunctionTable : IFunctionTableLookup
    {
        void Add(FunctionDefinition func);
        void Delete(FunctionDefinition func);
    }
}
