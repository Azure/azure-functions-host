using System;
using System.IO;
using System.Net;
using System.Threading;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob.Protocol;
using System.Text;

namespace Microsoft.WindowsAzure.Jobs
{
    // Helper class to manage releasing the blob lease. 
    internal class BlobLeaseHolder : IBlobLeaseHolder, IDisposable
    {
        ICloudBlob _blob;
        string _leaseId;

        public string LeaseId
        {
            get
            {
                return _leaseId;
            }
        }

        public void BlockUntilAcquired(ICloudBlob blob)
        {
            if (_blob != null)
            {
                throw new InvalidOperationException("Can't reacquire a blob lease");
            }
            _blob = blob;

            CloudBlobPath path = new CloudBlobPath(blob);

            // $$$ Fix this, this can deadlock if we have multiple lease parameters?
            // only have 1 lease parameter, so shouldn't happen. Either enforce that, or break the deadlock. 
            while (true)
            {
                _leaseId = BlobLease.TryAquireLease(blob);
                if (_leaseId != null)
                {
                    break;
                }

                Console.WriteLine("Blocked waiting on lease for blob: {0}", path);
                Thread.Sleep(2 * 1000);
            }
        }

        // Suppress release on this object, and transfer ownership to a new holder.
        public IBlobLeaseHolder TransferOwnership()
        {
            var x = new BlobLeaseHolder { _blob = _blob, _leaseId = _leaseId };
            _leaseId = null; // Suppress
            return x;
        }

        public void Dispose()
        {
            if (_leaseId != null)
            {
                BlobLease.ReleaseLease(_blob, _leaseId);
            }
        }

        public void UploadText(string text)
        {
            BlobLease.UploadText(_blob, text, _leaseId);
        }


        // From http://blog.smarx.com/posts/leasing-windows-azure-blobs-using-the-storage-client-library
        private static class BlobLease
        {
            // Try to acquire a lease on the blob. 
            // Return null if the lease is already held (caller can then throw, retry+poll, etc)
            public static string TryAquireLease(ICloudBlob blob)
            {
                try
                {
                    return blob.AcquireLease(leaseTime: null, proposedLeaseId: null);
                }
                catch (StorageException e)
                {
                    if (e.RequestInformation.HttpStatusCode == 409)
                    {
                        // Lease already held. 

                        return null;
                    }
                    throw;
                }
            }

            public static void RenewLease(ICloudBlob blob, string leaseId)
            {
                blob.RenewLease(new AccessCondition { LeaseId = leaseId });

            }

            public static void ReleaseLease(ICloudBlob blob, string leaseId)
            {
                try
                {
                    blob.ReleaseLease(new AccessCondition { LeaseId = leaseId });
                }
                catch (StorageException)
                {
                    // 
                }
            }

            public static void BreakLease(ICloudBlob blob)
            {
                blob.BreakLease();
            }

            // NOTE: This method doesn't do everything that the regular UploadText does.
            // Notably, it doesn't update the BlobProperties of the blob (with the new
            // ETag and LastModifiedTimeUtc). It also, like all the methods in this file,
            // doesn't apply any retry logic. Use this at your own risk!
            public static void UploadText(ICloudBlob blob, string text, string leaseId)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                blob.UploadFromByteArray(bytes, 0, bytes.Length, accessCondition: new AccessCondition { LeaseId = leaseId });
            }
        }
    }
}
