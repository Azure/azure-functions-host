// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Dashboard.Data
{
    public class JsonVersionedDocumentStore<TDocument> : IVersionedDocumentStore<TDocument>
    {
        private static readonly JsonSerializerSettings _settings =
            JsonConcurrentDocumentStore<TDocument>.JsonSerializerSettings;

        private readonly IVersionedMetadataTextStore _innerStore;

        public JsonVersionedDocumentStore(IVersionedMetadataTextStore innerStore)
        {
            _innerStore = innerStore;
        }

        internal static JsonSerializerSettings JsonSerializerSettings
        {
            get { return _settings; }
        }

        public IEnumerable<VersionedMetadata> List(string prefix)
        {
            return _innerStore.List(prefix);
        }

        public VersionedMetadataDocument<TDocument> Read(string id)
        {
            VersionedMetadataText textItem = _innerStore.Read(id);

            if (textItem == null)
            {
                return null;
            }

            TDocument document = JsonConvert.DeserializeObject<TDocument>(textItem.Text);

            return new VersionedMetadataDocument<TDocument>(textItem.ETag, textItem.Metadata, textItem.Version,
                document);
        }

        public bool CreateOrUpdateIfLatest(string id, DateTimeOffset targetVersion,
            IDictionary<string, string> otherMetadata, TDocument document)
        {
            string text = JsonConvert.SerializeObject(document, _settings);

            return _innerStore.CreateOrUpdateIfLatest(id, targetVersion, otherMetadata, text);
        }

        public bool UpdateOrCreateIfLatest(string id, DateTimeOffset targetVersion,
            IDictionary<string, string> otherMetadata, TDocument document)
        {
            string text = JsonConvert.SerializeObject(document, _settings);

            return _innerStore.UpdateOrCreateIfLatest(id, targetVersion, otherMetadata, text);
        }

        public bool UpdateOrCreateIfLatest(string id, DateTimeOffset targetVersion,
            IDictionary<string, string> otherMetadata, TDocument document, string currentETag,
            DateTimeOffset currentVersion)
        {
            string text = JsonConvert.SerializeObject(document, _settings);

            return _innerStore.UpdateOrCreateIfLatest(id, targetVersion, otherMetadata, text, currentETag,
                currentVersion);
        }

        public bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion)
        {
            return _innerStore.DeleteIfLatest(id, deleteThroughVersion);
        }

        public bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion, string currentETag,
            DateTimeOffset currentVersion)
        {
            return _innerStore.DeleteIfLatest(id, deleteThroughVersion, currentETag, currentVersion);
        }
    }
}
