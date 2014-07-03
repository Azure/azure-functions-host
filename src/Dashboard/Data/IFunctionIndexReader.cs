using System;

namespace Dashboard.Data
{
    public interface IFunctionIndexReader
    {
        DateTimeOffset GetCurrentVersion();

        IResultSegment<FunctionSnapshot> Read(int maximumResults, string continuationToken);
    }
}
