namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IFunctionTableLookup
    {
        // Function Id is the location.ToString().
        FunctionDefinition Lookup(string functionId);
        FunctionDefinition[] ReadAll();
    }
}
