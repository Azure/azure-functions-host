namespace Dashboard.Data
{
    public interface IConcurrentDocumentStore<TDocument>
    {
        IConcurrentDocument<TDocument> Read(string id);

        void CreateOrUpdate(string id, TDocument document);

        bool TryCreate(string id, TDocument document);

        bool TryUpdate(string id, string eTag, TDocument document);

        bool TryDelete(string id, string eTag);
    }
}
