namespace Dashboard.Data
{
    public interface IConcurrentText
    {
        string ETag { get; }

        string Text { get; }
    }
}
