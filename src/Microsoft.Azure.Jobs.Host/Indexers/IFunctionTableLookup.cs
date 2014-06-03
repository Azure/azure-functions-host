namespace Microsoft.Azure.Jobs
{
    internal interface IFunctionTableLookup
    {
        FunctionDefinition Lookup(string functionId);
        FunctionDefinition[] ReadAll();
    }
}
