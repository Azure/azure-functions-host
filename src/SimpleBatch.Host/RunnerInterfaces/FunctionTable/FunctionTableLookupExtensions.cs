namespace Microsoft.WindowsAzure.Jobs
{
    internal static class FunctionTableLookupExtensions
    {
        public static FunctionDefinition Lookup(this IFunctionTableLookup lookup, FunctionLocation location)
        {
            string rowKey = location.ToString();
            return lookup.Lookup(rowKey);
        }
    }
}
