using System;

namespace Dashboard.Data
{
    [CLSCompliant(false)]
    public interface IFunctionStatisticsReader
    {
        FunctionStatisticsEntity Lookup(string functionId);
    }
}
