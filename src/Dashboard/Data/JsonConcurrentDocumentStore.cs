// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Protocols;
using Newtonsoft.Json;

namespace Dashboard.Data
{
    public class JsonConcurrentDocumentStore<TDocument> : IConcurrentDocumentStore<TDocument>,
        IConcurrentMetadataDocumentStore<TDocument>
    {
        private static readonly JsonSerializerSettings _settings = JsonSerialization.Settings;

        private readonly IConcurrentMetadataTextStore _innerStore;

        public JsonConcurrentDocumentStore(IConcurrentMetadataTextStore innerStore)
        {
            _innerStore = innerStore;
        }

        internal static JsonSerializerSettings JsonSerializerSettings
        {
            get { return _settings; }
        }

        public IEnumerable<ConcurrentMetadata> List(string prefix)
        {
            return _innerStore.List(prefix);
        }

        IConcurrentDocument<TDocument> IConcurrentDocumentStore<TDocument>.Read(string id)
        {
            return ((IConcurrentMetadataDocumentStore<TDocument>)this).Read(id);
        }

        public ConcurrentMetadataDocument<TDocument> Read(string id)
        {
            ConcurrentMetadataText innerResult = _innerStore.Read(id);

            if (innerResult == null)
            {
                return null;
            }

            string eTag = innerResult.ETag;
            IDictionary<string, string> metadata = innerResult.Metadata;
            TDocument document = JsonConvert.DeserializeObject<TDocument>(innerResult.Text, _settings);
            return new ConcurrentMetadataDocument<TDocument>(eTag, metadata, document);
        }

        public void CreateOrUpdate(string id, TDocument document)
        {
            string text = JsonConvert.SerializeObject(document, _settings);

            _innerStore.CreateOrUpdate(id, text);
        }

        public bool TryCreate(string id, TDocument document)
        {
            return TryCreate(id, metadata: null, document: document);
        }

        public bool TryCreate(string id, IDictionary<string, string> metadata, TDocument document)
        {
            string text = JsonConvert.SerializeObject(document, _settings);

            return _innerStore.TryCreate(id, metadata, text);
        }

        public bool TryUpdate(string id, string eTag, TDocument document)
        {
            return TryUpdate(id, eTag, metadata: null, document: document);
        }

        public bool TryUpdate(string id, string eTag, IDictionary<string, string> metadata, TDocument document)
        {
            string text = JsonConvert.SerializeObject(document, _settings);

            return _innerStore.TryUpdate(id, eTag, metadata, text);
        }

        public bool TryDelete(string id, string eTag)
        {
            return _innerStore.TryDelete(id, eTag);
        }
    }
}
