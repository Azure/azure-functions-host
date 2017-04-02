// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Storage
{
    internal static class StorageAccountTypeExtensions
    {
        public static string GetFriendlyDescription(this StorageAccountType type)
        {
            switch (type)
            {
                case StorageAccountType.Premium:
                    return "Premium";

                case StorageAccountType.BlobOnly:
                    return "Blob-Only/ZRS";

                case StorageAccountType.GeneralPurpose:
                default:
                    return "General Purpose";
            }
        }
    }
}