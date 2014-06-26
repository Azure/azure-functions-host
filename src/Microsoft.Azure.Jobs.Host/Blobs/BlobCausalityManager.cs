using System;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
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
        public static void SetWriter(ICloudBlob blob, Guid function)
        {
            // Beware, SetMetadata() is like a POST, not a PUT, so must
            // fetch existing attributes to preserve them. 
            try
            {
                blob.FetchAttributes();
            }
            catch (StorageException)
            {
                // blob has been deleted. 
                return;
            }

            blob.Metadata[MetadataKeyName] = function.ToString();
            blob.SetMetadata();
        }

        public static Guid? GetWriter(ICloudBlob blob)
        {
            blob.FetchAttributes();
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
