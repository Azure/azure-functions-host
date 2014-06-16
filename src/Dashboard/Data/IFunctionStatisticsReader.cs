using System;

namespace Dashboard.Data
{
    [CLSCompliant(false)]
    public interface IFunctionStatisticsReader
    {
        FunctionStatistics Lookup(string functionId);
    }
}
