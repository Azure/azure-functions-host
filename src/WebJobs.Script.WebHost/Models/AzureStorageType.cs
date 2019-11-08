// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.Serialization;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public enum AzureStorageType
    {
        /// <summary>
        /// Azure Files Storage.
        /// </summary>
        [EnumMember]
        AzureFiles = 0,

        /// <summary>
        /// Azure Blob Storage.
        /// </summary>
        [EnumMember]
        AzureBlob = 1
    }
}