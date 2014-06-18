namespace Dashboard.Data
{
    public interface IVersionedTextStore
    {
        VersionedText Read(string id);

        void CreateOrUpdate(string id, string text);

        void DeleteIfExists(string id);

        bool TryCreate(string id, string text);

        bool TryUpdate(string id, string text, string eTag);

        bool TryDelete(string id, string eTag);
    }
}
