// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a blob in the same storage account as the entity that references it.</summary>
#if PUBLICPROTOCOL
    public class LocalBlobDescriptor
#else
    internal class LocalBlobDescriptor
#endif
    {
        /// <summary>Gets or sets the container name.</summary>
        public string ContainerName { get; set; }

        /// <summary>Gets or sets the blob name.</summary>
        public string BlobName { get; set; }
    }
}
