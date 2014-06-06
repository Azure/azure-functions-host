namespace Dashboard.Data
{
    public interface IVersionedDocumentStore<TDocument>
    {
        VersionedDocument<TDocument> Read(string id);

        void CreateOrUpdate(string id, TDocument document);

        bool TryCreate(string id, TDocument document);

        bool TryUpdate(string id, TDocument document, string eTag);

        bool TryDelete(string id, string eTag);
    }
}
