using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public interface IVersionedDocumentStore<TDocument>
    {
        IEnumerable<VersionedMetadata> List(string prefix);

        VersionedMetadataDocument<TDocument> Read(string id);

        bool CreateOrUpdateIfLatest(string id, IDictionary<string, string> metadata, TDocument document);

        bool UpdateOrCreateIfLatest(string id, IDictionary<string, string> metadata, TDocument document);

        bool UpdateOrCreateIfLatest(string id, IDictionary<string, string> metadata, TDocument document,
            string currentETag, DateTimeOffset currentVersion);

        bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion);

        bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion, string currentETag,
            DateTimeOffset currentVersion);
    }
}
