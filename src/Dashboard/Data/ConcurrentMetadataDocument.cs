// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class ConcurrentMetadataDocument<TDocument> : IConcurrentDocument<TDocument>
    {
        private readonly string _eTag;
        private readonly IDictionary<string, string> _metadata;
        private readonly TDocument _document;

        public ConcurrentMetadataDocument(string eTag, IDictionary<string, string> metadata, TDocument document)
        {
            if (eTag == null)
            {
                throw new ArgumentNullException("eTag");
            }
            else if (metadata == null)
            {
                throw new ArgumentNullException("metadata");
            }

            // document may be null (valid JSON serialization of text bytes: "null").

            _eTag = eTag;
            _metadata = metadata;
            _document = document;
        }

        public string ETag { get { return _eTag; } }

        public IDictionary<string, string> Metadata { get { return _metadata; } }

        public TDocument Document { get { return _document; } }
    }
}
