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
        public static int ScanBlobDir(CloudStorageAccount account, CloudBlobPath path)
        {
            var settings = Services.GetOrchestratorSettings();
            var worker = new Orchestrator.Worker(settings);     

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
        }
    }
}

