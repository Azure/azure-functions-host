using System;

namespace Dashboard.Data
{
    public interface IFunctionStatisticsReader
    {
        FunctionStatistics Lookup(string functionId);
    }
}
