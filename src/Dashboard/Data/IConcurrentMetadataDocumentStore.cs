// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
namespace Dashboard.Data
{
    public interface IConcurrentMetadataDocumentStore<TDocument> : IConcurrentDocumentStore<TDocument>
    {
        IEnumerable<ConcurrentMetadata> List(string prefix);

        new ConcurrentMetadataDocument<TDocument> Read(string id);

        bool TryCreate(string id, IDictionary<string, string> metadata, TDocument document);

        bool TryUpdate(string id, string eTag, IDictionary<string, string> metadata, TDocument document);
    }
}
