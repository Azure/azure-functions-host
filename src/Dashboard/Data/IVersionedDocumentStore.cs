using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public interface IVersionedDocumentStore<TDocument>
    {
        IEnumerable<VersionedMetadata> List(string prefix);

        VersionedMetadataDocument<TDocument> Read(string id);

        bool CreateOrUpdateIfLatest(string id, DateTimeOffset targetVersion, IDictionary<string, string> otherMetadata,
            TDocument document);

        bool UpdateOrCreateIfLatest(string id, DateTimeOffset targetVersion, IDictionary<string, string> otherMetadata,
            TDocument document);

        bool UpdateOrCreateIfLatest(string id, DateTimeOffset targetVersion, IDictionary<string, string> otherMetadata,
            TDocument document, string currentETag, DateTimeOffset currentVersion);

        bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion);

        bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion, string currentETag,
            DateTimeOffset currentVersion);
    }
}
