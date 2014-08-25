// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class BlobTriggerMessage
    {
        public string FunctionId { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public BlobType BlobType { get; set; }

        public string ContainerName { get; set; }

        public string BlobName { get; set; }

        public string ETag { get; set; }
    }
}
