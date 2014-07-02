namespace Dashboard.Data
{
    public interface IFunctionIndexReader
    {
        IResultSegment<FunctionSnapshot> Read(int maximumResults, string continuationToken);
    }
}
