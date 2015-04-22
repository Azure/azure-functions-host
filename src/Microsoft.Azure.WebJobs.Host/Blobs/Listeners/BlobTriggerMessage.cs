// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class BlobTriggerMessage
    {
        public string Type { get { return "BlobTrigger"; } }

        public string FunctionId { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public StorageBlobType BlobType { get; set; }

        public string ContainerName { get; set; }

        public string BlobName { get; set; }

        public string ETag { get; set; }
    }
}
