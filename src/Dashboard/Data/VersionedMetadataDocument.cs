// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class VersionedMetadataDocument<TDocument>
    {
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

            ETag = eTag;
            Metadata = metadata;
            Version = version;
            Document = document;
        }

        public string ETag { get; private set; }

        public IDictionary<string, string> Metadata { get; private set; }

        public DateTimeOffset Version { get; private set; }

        public TDocument Document { get; private set; }
    }
}
