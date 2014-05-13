using System;

namespace Dashboard.Data
{
    internal interface IFunctionInstanceLookup
    {
        FunctionInstanceSnapshot Lookup(Guid id);
    }
}
