// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class ConcurrentMetadataText : IConcurrentText
    {
        private readonly string _eTag;
        private readonly IDictionary<string, string> _metadata;
        private readonly string _text;

        public ConcurrentMetadataText(string eTag, IDictionary<string, string> metadata, string text)
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
            _text = text;
        }

        public string ETag { get { return _eTag; } }

        public IDictionary<string, string> Metadata { get { return _metadata; } }

        public string Text { get { return _text; } }
    }
}
