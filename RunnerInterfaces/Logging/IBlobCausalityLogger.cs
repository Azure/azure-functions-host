using System;
using System.Collections.Generic;
using System.Diagnostics;
using Executor;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    public interface IBlobCausalityLogger
    {
        // !!! ICloudBlobBinder uses string Container/Blob names, not CloudBlob

        // Records which function wrote to this blob
        void SetWriter(CloudBlob blob, Guid function);

        Guid GetWriter(CloudBlob blob);
    }

    // Tracks which function wrote each blob via blob metadata. 
    // This may be risky because it does interfere with the function (and the user could tamper with it
    // or accidentally remove it).
    // An alternative mechanism would be to have a look-aside table. But that's risky because it's
    // a separate object to manage and could get out of sync.
    public class BlobCausalityLogger : IBlobCausalityLogger
    {
        // Metadata names must adehere to C# identifier rules
        // http://msdn.microsoft.com/en-us/library/windowsazure/dd135715.aspx
        const string MetadataKeyName = "SimpleBatch_WriterFunc";

        [DebuggerNonUserCode] // ignore the StorageClientException in debugger.
        public void SetWriter(CloudBlob blob, Guid function)
        {
            // Beware, SetMetadata() is like a POST, not a PUT, so must
            // fetch existing attributes to preserve them. 
            try
            {
                blob.FetchAttributes();
            }
            catch (StorageClientException)
            {
                // blob has been deleted. 
                return;
            }

            blob.Metadata[MetadataKeyName] = function.ToString();
            blob.SetMetadata();
        }

        public Guid GetWriter(CloudBlob blob)
        {
            
            blob.FetchAttributes();
            string val = blob.Metadata[MetadataKeyName];
            if (val == null)
            {
                return Guid.Empty;
            }
            Guid result;
            bool success = Guid.TryParse(val, out result);
            // $$$, What should we do on parse failure? Ignore?

            return result;
        }
    }
}