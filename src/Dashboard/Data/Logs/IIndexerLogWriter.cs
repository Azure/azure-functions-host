namespace Dashboard.Data.Logs
{
    public interface IIndexerLogWriter
    {
        void Write(IndexerLogEntry entry);
    }
}
