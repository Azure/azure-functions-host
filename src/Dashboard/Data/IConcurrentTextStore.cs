namespace Dashboard.Data
{
    public interface IConcurrentTextStore
    {
        IConcurrentText Read(string id);

        void CreateOrUpdate(string id, string text);

        void DeleteIfExists(string id);

        bool TryCreate(string id, string text);

        bool TryUpdate(string id, string eTag, string text);

        bool TryDelete(string id, string eTag);
    }
}
