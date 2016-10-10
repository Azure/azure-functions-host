// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Defines an append blob.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageAppendBlob : IStorageBlob
#else
    internal interface IStorageAppendBlob : IStorageBlob
#endif
    {
        /// <summary>Gets the underlying <see cref="CloudAppendBlob"/>.</summary>
        new CloudAppendBlob SdkObject { get; }

        /// <summary>Uploads text contents to the blob.</summary>
        /// <param name="content">The text to upload.</param>
        /// <param name="encoding">The text encoding to use.</param>
        /// <param name="accessCondition">The condition that must be met for the request to succeed.</param>
        /// <param name="options">The options for the request.</param>
        /// <param name="operationContext">The operation context for the request.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will upload text contents to the blob.</returns>
        Task UploadTextAsync(string content, Encoding encoding = null, AccessCondition accessCondition = null,
            BlobRequestOptions options = null, OperationContext operationContext = null,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}