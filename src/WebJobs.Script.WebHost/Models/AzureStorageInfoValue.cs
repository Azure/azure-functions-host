// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    /// <summary>
    /// Azure Files or Blob Storage access information.
    /// </summary>
    public class AzureStorageInfoValue
    {
        public const string AzureFilesStoragePrefix = "AZUREFILESSTORAGE_";
        public const string AzureBlobStoragePrefix = "AZUREBLOBSTORAGE_";

        public AzureStorageInfoValue(string id, AzureStorageType type, string accountName, string shareName,
            string accessKey, string mountPath)
        {
            Id = id;
            Type = type;
            AccountName = accountName;
            ShareName = shareName;
            AccessKey = accessKey;
            MountPath = mountPath;
        }

        /// <summary>
        /// Gets identifier for storage info.
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// Gets type of storage.
        /// </summary>
        public AzureStorageType Type { get; private set; }

        /// <summary>
        /// Gets name of the storage account.
        /// </summary>
        public string AccountName { get; private set; }

        /// <summary>
        /// Gets name of the file share (container name, for Blob storage).
        /// </summary>
        public string ShareName { get; private set; }

        /// <summary>
        /// Gets access key for the storage account.
        /// </summary>
        public string AccessKey { get; private set; }

        /// <summary>
        /// Gets path to mount the storage within the site's runtime environment.
        /// </summary>
        public string MountPath { get; private set; }

        public static AzureStorageInfoValue FromEnvironmentVariable(KeyValuePair<string, string> environmentVariable)
        {
            string id;
            AzureStorageType type;

            var name = environmentVariable.Key;

            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (name.StartsWith(AzureFilesStoragePrefix, StringComparison.OrdinalIgnoreCase))
            {
                id = name.Substring(AzureFilesStoragePrefix.Length);
                type = AzureStorageType.AzureFiles;
            }
            else if (name.StartsWith(AzureBlobStoragePrefix, StringComparison.OrdinalIgnoreCase))
            {
                id = name.Substring(AzureBlobStoragePrefix.Length);
                type = AzureStorageType.AzureBlob;
            }
            else
            {
                return null;
            }

            var parts = environmentVariable.Value?.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts == null)
            {
                return null;
            }

            if (parts.Length != 4 && parts.Length != 5)
            {
                return null;
            }

            var accountName = parts[0];
            var shareName = parts[1];
            var accessKey = parts[2];
            var mountPath = parts[3];

            return new AzureStorageInfoValue(id, type, accountName, shareName, accessKey, mountPath);
        }
    }
}
