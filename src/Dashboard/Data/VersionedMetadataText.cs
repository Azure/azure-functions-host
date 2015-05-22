// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class VersionedMetadataText
    {
        public VersionedMetadataText(string eTag, IDictionary<string, string> metadata, DateTimeOffset version, string text)
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

            ETag = eTag;
            Metadata = metadata;
            Version = version;
            Text = text;
        }

        public string ETag { get; private set; }

        public IDictionary<string, string> Metadata { get; private set; }

        public DateTimeOffset Version { get; private set; }

        public string Text { get; private set; }
    }
}
