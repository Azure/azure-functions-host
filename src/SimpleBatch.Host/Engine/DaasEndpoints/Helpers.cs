using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class Helpers
    {
        // Queue execution for any blobs in the given path
        // conatiner\blob1\blobsubdir
        // Returns count scanned
        public static int ScanBlobDir(Services services, CloudStorageAccount account, CloudBlobPath path)
        {
            // $$$ Need to determine FunctionDefinition from the given blob. 
            throw new NotImplementedException();
        }
    }
}
