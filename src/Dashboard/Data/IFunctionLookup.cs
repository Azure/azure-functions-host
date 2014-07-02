namespace Dashboard.Data
{
    internal interface IFunctionLookup
    {
        FunctionSnapshot Read(string functionId);
    }
}
