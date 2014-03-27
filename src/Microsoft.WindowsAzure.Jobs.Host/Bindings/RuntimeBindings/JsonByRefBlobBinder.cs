using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.WindowsAzure.Jobs
{
    // 2-way serialize as a rich object. 
    // Uses leases.
    class JsonByRefBlobBinder : ICloudBlobBinder
    {
        public IBlobLeaseHolder Lease { get; set; }

        public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
        {
            CloudBlockBlob blob = BlobClient.GetBlockBlob(binder.AccountConnectionString, containerName, blobName);

            object result;
            string json = string.Empty;
            if (BlobClient.DoesBlobExist(blob))
            {
                // If blob was just created, it may be 0-length, which is not valid JSON.
                json = blob.DownloadText();
            }

            if (!string.IsNullOrWhiteSpace(json))
            {
                result = ObjectBinderHelpers.DeserializeObject(json, targetType);
            }
            else
            {
                result = NewDefault(targetType);
            }
            
            var x = new BindCleanupResult
            {
                Result = result
            };

            // $$$ There's still a far distance between here and releasing the lease in BindResult.Cleanup. 
            // Could have an orphaned lease. 
            var newLease = Lease.TransferOwnership();

            x.Cleanup = () =>
                {
                    object newResult = x.Result;
                    using (newLease)
                    {
                        string json2 = ObjectBinderHelpers.SerializeObject(newResult, targetType);
                        newLease.UploadText(json2);
                    }                    
                };
            return x;
        }

        static object NewDefault(Type targetType)
        {
            if (targetType.IsClass)
            {
                return null;
            }
            return Activator.CreateInstance(targetType);
        }
    }
}
