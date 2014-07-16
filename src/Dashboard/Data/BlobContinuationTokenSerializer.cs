// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    internal static class BlobContinuationTokenSerializer
    {
        public static string Serialize(BlobContinuationToken token)
        {
            if (token == null)
            {
                return null;
            }

            // Include TargetLocation in serialized form to ensure consistent results for RA-GRS accounts. (Without it,
            // each request could see different sides of eventually consisent replication).
            // See: http://blogs.msdn.com/b/windowsazurestorage/archive/2013/12/04/ (ignore source line break)
            // introducing-read-access-geo-replicated-storage-ra-grs-for-windows-azure-storage.aspx

            // Prefix the NextMarker with a single character indicating TargetLocation.
            return Serialize(token.TargetLocation) + token.NextMarker;
        }

        public static BlobContinuationToken Deserialize(string value)
        {
            if (value == null)
            {
                return null;
            }

            if (value.Length == 0)
            {
                throw new FormatException("Serialized continuation tokens must not be empty strings.");
            }

            // First character is TargetLocation indicator; remainder is NextMarker.
            char locationPortion = value[0];
            string nextMarkerPortion = value.Substring(1);

            return new BlobContinuationToken
            {
                TargetLocation = Deserialize(locationPortion),
                NextMarker = nextMarkerPortion
            };
        }

        private static char Serialize(StorageLocation? location)
        {
            if (!location.HasValue)
            {
                return '*'; // Primary or Secondary
            }
            else
            {
                StorageLocation locationValue = location.Value;

                switch (locationValue)
                {
                    case StorageLocation.Primary:
                        return 'P';
                    case StorageLocation.Secondary:
                        return 'S';
                    default:
                        throw new ArgumentOutOfRangeException("Unknown StorageLocation.", "location");
                }
            }
        }

        private static StorageLocation? Deserialize(char value)
        {
            switch (value)
            {
                case '*':
                    return null; // Primary or Secondary
                case 'P':
                    return StorageLocation.Primary;
                case 'S':
                    return StorageLocation.Secondary;
                default:
                    throw new ArgumentOutOfRangeException("Unknown serialized StorageLocation.", "value");
            }
        }
    }
}
