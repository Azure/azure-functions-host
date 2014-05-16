using System.Collections.Generic;

namespace Dashboard.Data
{
    internal interface IFunctionLookup
    {
        FunctionSnapshot Read(string functionId);

        IReadOnlyList<FunctionSnapshot> ReadAll();
    }
}
