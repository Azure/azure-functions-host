// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class ConcurrentMetadata
    {
        private readonly string _id;
        private readonly string _eTag;
        private readonly IDictionary<string, string> _metadata;

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

            _id = id;
            _eTag = eTag;
            _metadata = metadata;
        }

        public string Id { get { return _id; } }

        public string ETag { get { return _eTag; } }

        public IDictionary<string, string> Metadata { get { return _metadata; } }
    }
}
