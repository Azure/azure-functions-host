using System;
using System.IO;
using System.Net;
using System.Threading;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure.StorageClient.Protocol;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IBlobLeaseHolder : IDisposable
    {
        void BlockUntilAcquired(CloudBlob blob);
        // release via Dipose()
        IBlobLeaseHolder TransferOwnership();

        void UploadText(string text);
    }

    // Helper class to manage releasing the blob lease. 
    internal class BlobLeaseHolder : IBlobLeaseHolder, IDisposable
    {
        CloudBlob _blob;
        string _leaseId;

        public string LeaseId
        {
            get
            {
                return _leaseId;
            }
        }

        public void BlockUntilAcquired(CloudBlob blob)
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
            public static string TryAquireLease(CloudBlob blob)
            {
                // See http://msdn.microsoft.com/en-us/library/windowsazure/ee691972.aspx 
                //for docs on REST APi. 
                try
                {
                    var creds = blob.ServiceClient.Credentials;
                    var transformedUri = new Uri(creds.TransformUri(blob.Uri.ToString()));
                    var req = BlobRequest.Lease(transformedUri,
                        90, // timeout (in seconds)                    
                        LeaseAction.Acquire, // as opposed to "break" "release" or "renew"
                        null); // name of the existing lease, if any

                    req.Headers.Add("x-ms-lease-duration", "-1");  // Lease duration, infinite. 
                    req.Headers["x-ms-version"] = "2012-02-12"; // need to overwrite version for infinite leases. 


                    blob.ServiceClient.Credentials.SignRequest(req); // Do this after request is fully formed. 
                    using (var response = req.GetResponse())
                    {
                        return response.Headers["x-ms-lease-id"];
                    }
                }
                catch (WebException e)
                {
                    var x = (HttpWebResponse)e.Response;

                    string body = new StreamReader(x.GetResponseStream()).ReadToEnd();

                    if (x.StatusCode == HttpStatusCode.Conflict)
                    {
                        // Lease already held. 

                        return null;
                    }
                    throw;
                }
            }

            private static void DoLeaseOperation(CloudBlob blob, string leaseId, LeaseAction action)
            {
                var creds = blob.ServiceClient.Credentials;
                var transformedUri = new Uri(creds.TransformUri(blob.Uri.ToString()));
                var req = BlobRequest.Lease(transformedUri, 90, action, leaseId);
                creds.SignRequest(req);
                req.GetResponse().Close();
            }

            public static void RenewLease(CloudBlob blob, string leaseId)
            {
                DoLeaseOperation(blob, leaseId, LeaseAction.Release);
            }

            public static void ReleaseLease(CloudBlob blob, string leaseId)
            {
                try
                {
                    DoLeaseOperation(blob, leaseId, LeaseAction.Release);
                }
                catch (WebException)
                {
                    // 
                }
            }

            public static void BreakLease(CloudBlob blob)
            {
                DoLeaseOperation(blob, null, LeaseAction.Break);
            }

            // NOTE: This method doesn't do everything that the regular UploadText does.
            // Notably, it doesn't update the BlobProperties of the blob (with the new
            // ETag and LastModifiedTimeUtc). It also, like all the methods in this file,
            // doesn't apply any retry logic. Use this at your own risk!
            public static void UploadText(CloudBlob blob, string text, string leaseId)
            {
                string url = blob.Uri.ToString();
                if (blob.ServiceClient.Credentials.NeedsTransformUri)
                {
                    url = blob.ServiceClient.Credentials.TransformUri(url);
                }
                var req = BlobRequest.Put(new Uri(url), 90, new BlobProperties(), BlobType.BlockBlob, leaseId, 0);
                using (var writer = new StreamWriter(req.GetRequestStream()))
                {
                    writer.Write(text);
                }
                blob.ServiceClient.Credentials.SignRequest(req);
                req.GetResponse().Close();
            }
        }
    }
}