// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class VersionedMetadataText
    {
        private readonly string _eTag;
        private readonly IDictionary<string, string> _metadata;
        private readonly DateTimeOffset _version;
        private readonly string _text;

        public VersionedMetadataText(string eTag, IDictionary<string, string> metadata, DateTimeOffset version,
            string text)
        {
            if (eTag == null)
            {
                throw new ArgumentNullException("eTag");
            }
            else if (metadata == null)
            {
                throw new ArgumentNullException("metadata");
            }
            else if (text == null)
            {
                throw new ArgumentNullException("text");
            }

            _eTag = eTag;
            _metadata = metadata;
            _version = version;
            _text = text;
        }

        public string ETag { get { return _eTag; } }

        public IDictionary<string, string> Metadata { get { return _metadata; } }

        public DateTimeOffset Version { get { return _version; } }

        public string Text { get { return _text; } }
    }
}
