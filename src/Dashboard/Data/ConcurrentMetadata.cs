// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class ConcurrentMetadata
    {
        public ConcurrentMetadata(string id, string eTag, IDictionary<string, string> metadata)
        {
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }
            else if (eTag == null)
            {
                throw new ArgumentNullException("eTag");
            }
            else if (metadata == null)
            {
                throw new ArgumentNullException("metadata");
            }

            Id = id;
            ETag = eTag;
            Metadata = metadata;
        }

        public string Id { get; private set; }

        public string ETag { get; private set; }

        public IDictionary<string, string> Metadata { get; private set; }
    }
}
