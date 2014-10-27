// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
    }
}
