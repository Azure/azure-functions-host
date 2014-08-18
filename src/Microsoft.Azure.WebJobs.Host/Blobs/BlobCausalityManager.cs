// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    // Tracks which function wrote each blob via blob metadata. 
    // This may be risky because it does interfere with the function (and the user could tamper with it
    // or accidentally remove it).
    // An alternative mechanism would be to have a look-aside table. But that's risky because it's
    // a separate object to manage and could get out of sync.
    internal static class BlobCausalityManager
    {
        // Metadata names must adehere to C# identifier rules
        // http://msdn.microsoft.com/en-us/library/windowsazure/dd135715.aspx
        const string MetadataKeyName = "AzureJobsParentId";

        [DebuggerNonUserCode] // ignore the StorageClientException in debugger.
        public static async Task SetWriterAsync(ICloudBlob blob, Guid function, CancellationToken cancellationToken)
        {
            // Beware, SetMetadata() is like a POST, not a PUT, so must
            // fetch existing attributes to preserve them. 
            try
            {
                await blob.FetchAttributesAsync(cancellationToken);
            }
            catch (StorageException)
            {
                // blob has been deleted. 
                return;
            }

            blob.Metadata[MetadataKeyName] = function.ToString();
            await blob.SetMetadataAsync(cancellationToken);
        }

        public static async Task<Guid?> GetWriterAsync(ICloudBlob blob, CancellationToken cancellationToken)
        {
            await blob.FetchAttributesAsync(cancellationToken);
            if (!blob.Metadata.ContainsKey(MetadataKeyName))
            {
                return null;
            }
            string val = blob.Metadata[MetadataKeyName];
            Guid result;
            if (Guid.TryParse(val, out result))
            {
                return result;
            }

            return null;
        }
    }
}
