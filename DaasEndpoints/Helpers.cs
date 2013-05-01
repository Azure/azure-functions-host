using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;

namespace DaasEndpoints
{
    public static class Helpers
    {
        // Queue execution for any blobs in the given path
        // conatiner\blob1\blobsubdir
        // Returns count scanned
        public static int ScanBlobDir(Services services, CloudStorageAccount account, CloudBlobPath path)
        {
            // !!! Need to determine FunctionDefinition from the given blob. 
            throw new NotImplementedException();
#if false            
            var worker = services.GetOrchestrationWorker();

            int count = 0;            
            foreach (IListBlobItem blobItem in path.ListBlobsInDir(account))
            {
                CloudBlob b = blobItem as CloudBlob;
                if (b != null)
                {
                    // Produce an invocation record and queue it. 
                    worker.OnNewBlob(b);
                    count++;
                }
            }
            return count;
#endif
        }
    }
}

