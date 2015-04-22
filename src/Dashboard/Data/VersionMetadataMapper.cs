// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Dashboard.Data
{
    public class VersionMetadataMapper : IVersionMetadataMapper
    {
        private const string VersionMetadataKey = "Version";

        private static readonly VersionMetadataMapper _instance = new VersionMetadataMapper();

        private VersionMetadataMapper()
        {
        }

        public static VersionMetadataMapper Instance
        {
            get { return _instance; }
        }

        public DateTimeOffset GetVersion(IDictionary<string, string> metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException("metadata");
            }

            if (!metadata.ContainsKey(VersionMetadataKey))
            {
                return DateTimeOffset.MinValue;
            }

            string unparsed = metadata[VersionMetadataKey];
            DateTimeOffset parsed;

            if (!DateTimeOffset.TryParseExact(unparsed, "o", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out parsed))
            {
                return DateTimeOffset.MinValue;
            }

            return parsed;
        }

        public void SetVersion(DateTimeOffset version, IDictionary<string, string> metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException("metadata");
            }

            metadata[VersionMetadataKey] = version.ToString("o");
        }
    }
}
