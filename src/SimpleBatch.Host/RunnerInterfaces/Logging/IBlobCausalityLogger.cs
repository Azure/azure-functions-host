using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // Records the function instance that created a blob. 
    // Used to track blob causality. This lets functions that read from a blob know who wrote that blob, and thus
    // deduce a parent relationship. 
    internal interface IBlobCausalityLogger
    {
        // Records which function wrote to this blob
        // blob - the blob that was written
        // function - instance guid for that last wrote this blob's contents. 
        void SetWriter(CloudBlob blob, Guid function);

        Guid GetWriter(CloudBlob blob);
    }
}
