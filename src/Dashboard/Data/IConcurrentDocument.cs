namespace Dashboard.Data
{
    public interface IConcurrentDocument<TDocument>
    {
        string ETag { get; }

        TDocument Document { get; }
    }
}
