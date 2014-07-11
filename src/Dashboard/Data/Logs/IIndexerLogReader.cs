namespace Dashboard.Data.Logs
{
    public interface IIndexerLogReader
    {
        IndexerLogEntry ReadWithDetails(string logEntryId);

        IResultSegment<IndexerLogEntry> ReadWithoutDetails(int maximumResults, string continuationToken);
    }
}