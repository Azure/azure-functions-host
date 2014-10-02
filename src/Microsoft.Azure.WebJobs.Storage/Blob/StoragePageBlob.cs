// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Represents a page blob.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StoragePageBlob : IStoragePageBlob
#else
    internal class StoragePageBlob : IStoragePageBlob
#endif
    {
        private readonly IStorageBlobContainer _parent;
        private readonly CloudPageBlob _sdk;

        /// <summary>Initializes a new instance of the <see cref="CloudPageBlob"/> class.</summary>
        /// <param name="parent">The parent blob container.</param>
        /// <param name="sdk">The SDK blob to wrap.</param>
        public StoragePageBlob(IStorageBlobContainer parent, CloudPageBlob sdk)
        {
            _parent = parent;
            _sdk = sdk;
        }

        /// <inheritdoc />
        public StorageBlobType BlobType
        {
            get { return StorageBlobType.PageBlob; }
        }

        /// <inheritdoc />
        public IStorageBlobContainer Container
        {
            get { return _parent; }
        }

        /// <inheritdoc />
        public IDictionary<string, string> Metadata
        {
            get { return _sdk.Metadata; }
        }

        /// <inheritdoc />
        public string Name
        {
            get { return _sdk.Name; }
        }

        /// <inheritdoc />
        ICloudBlob IStorageBlob.SdkObject
        {
            get { return _sdk; }
        }

        /// <inheritdoc />
        public CloudPageBlob SdkObject
        {
            get { return _sdk; }
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return _sdk.ExistsAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task FetchAttributesAsync(CancellationToken cancellationToken)
        {
            return _sdk.FetchAttributesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            return _sdk.OpenReadAsync(cancellationToken);
        }
    }
}
