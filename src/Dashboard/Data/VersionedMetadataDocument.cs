// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class VersionedMetadataDocument<TDocument>
    {
        private readonly string _eTag;
        private readonly IDictionary<string, string> _metadata;
        private readonly DateTimeOffset _version;
        private readonly TDocument _document;

        public VersionedMetadataDocument(string eTag, IDictionary<string, string> metadata, DateTimeOffset version,
            TDocument document)
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
            _version = version;
            _document = document;
        }

        public string ETag { get { return _eTag; } }

        public IDictionary<string, string> Metadata { get { return _metadata; } }

        public DateTimeOffset Version { get { return _version; } }

        public TDocument Document { get { return _document; } }
    }
}
