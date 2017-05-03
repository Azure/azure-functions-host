// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Represents the system properties of a blob.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageBlobProperties : IStorageBlobProperties
#else
    internal class StorageBlobProperties : IStorageBlobProperties
#endif
    {
        private readonly ICloudBlob _sdk;

        /// <summary>Initializes a new instance of the <see cref="StorageBlobProperties"/> class.</summary>
        /// <param name="sdk">The SDK blob whose properties to wrap.</param>
        public StorageBlobProperties(ICloudBlob sdk)
        {
            _sdk = sdk;
        }

        /// <inheritdoc />
        public string ETag
        {
            get { return _sdk.Properties.ETag; }
        }

        /// <inheritdoc />
        public DateTimeOffset? LastModified
        {
            get { return _sdk.Properties.LastModified; }
        }

        /// <inheritdoc />
        public LeaseState LeaseState
        {
            get { return _sdk.Properties.LeaseState; }
        }

        /// <inheritdoc />
        public LeaseStatus LeaseStatus
        {
            get { return _sdk.Properties.LeaseStatus; }
        }

        /// <inheritdoc />
        public BlobProperties SdkObject
        {
            get { return _sdk.Properties; }
        }
    }
}
