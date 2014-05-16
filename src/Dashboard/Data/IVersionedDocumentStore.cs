namespace Dashboard.Data
{
    public interface IVersionedDocumentStore<TDocument>
    {
        VersionedDocument<TDocument> Read(string id);

        bool TryCreate(string id, TDocument document);

        bool TryUpdate(string id, VersionedDocument<TDocument> versionedDocument);

        bool TryDelete(string id, string eTag);
    }
}
