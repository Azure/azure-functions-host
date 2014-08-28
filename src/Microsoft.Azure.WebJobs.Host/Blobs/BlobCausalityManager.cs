// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    /// <summary>
    /// Tracks which function wrote each blob via blob metadata. 
    /// </summary>
    /// <remarks>
    /// This may be risky because it does interfere with the function (and the user could tamper with it
    /// or accidentally remove it).
    /// An alternative mechanism would be to have a look-aside table. But that's risky because it's
    /// a separate object to manage and could get out of sync.
    /// </remarks>
    internal static class BlobCausalityManager
    {
        // Metadata names must adehere to C# identifier rules
        // http://msdn.microsoft.com/en-us/library/windowsazure/dd135715.aspx
        internal const string MetadataKeyName = "AzureWebJobsParentId";

        [DebuggerNonUserCode] // ignore the StorageClientException in debugger.
        public static void SetWriter(IDictionary<string, string> metadata, Guid function)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException("metadata");
            }

            Debug.Assert(!Guid.Equals(Guid.Empty, function));
            
            metadata[MetadataKeyName] = function.ToString();
        }

        public static async Task<Guid?> GetWriterAsync(ICloudBlob blob, CancellationToken cancellationToken)
        {
            if (!await blob.TryFetchAttributesAsync(cancellationToken))
            {
                return null;
            }

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
