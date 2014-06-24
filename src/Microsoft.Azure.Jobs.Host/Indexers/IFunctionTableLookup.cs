namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal interface IFunctionTableLookup
    {
        FunctionDefinition Lookup(string functionId);
        FunctionDefinition[] ReadAll();
    }
}
