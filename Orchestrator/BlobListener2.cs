using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using System.Linq;

namespace Orchestrator
{
    // Experimental blob listener for large files
    // Just do new ones
    public class BlobListener2
    {
        HashSet<Uri> _seen = new HashSet<Uri>();

        // Containers to listen to, and timestamp of last poll
        // Use parallel arrays instead of dicts to allow update during enumeration
        CloudBlobContainer[] _containers;
        
        public BlobListener2(IEnumerable<CloudBlobContainer> containers)
        {
            _containers = containers.ToArray();
        }

        public void Poll(Action<CloudBlob> callback)
        {
            foreach(var container in _containers)
            {                
                Listen(container, callback);
            }
        }


        public void Listen(CloudBlobContainer container, Action<CloudBlob> callback)
        {
            var opt = new BlobRequestOptions();
            opt.UseFlatBlobListing = false; // directory 
            foreach (IListBlobItem blobItem in container.ListBlobs(opt))
            {
                if (_seen.Contains(blobItem.Uri))
                {
                    continue;
                }
                Listen(blobItem, callback);

                _seen.Add(blobItem.Uri);
            }
        }

        // See MSDN for recursing blobs:
        // http://msdn.microsoft.com/en-us/library/windowsazure/hh674669.aspx
        void Listen(IListBlobItem item, Action<CloudBlob> callback)
        {
            CloudBlobDirectory dir = item as CloudBlobDirectory;
            if (dir != null)
            {
                foreach (var subItem in dir.ListBlobs())
                {
                    Listen(subItem, callback);
                }
                return;
            }
            CloudBlob blob = (CloudBlob) item;
            //Console.WriteLine(blob.Uri);
            callback(blob);
        }
    }
}