using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk1
{
    // $$$ Get more aggressive testing here. 
    // We can Read a blob without a lease. 
    class MockBlobLeaseHolder : IBlobLeaseHolder
    {
        bool _ownLease;
        CloudBlob _blob;

        public static CloudBlob GetBlobSuffix(CloudBlob blob, string suffix)
        {
            var container = blob.Container;
            string name = blob.Name + suffix;
            CloudBlob blob2 = container.GetBlobReference(name);
            return blob2;
        }

        public void BlockUntilAcquired(CloudBlob blob)
        {
            Assert.IsFalse(_ownLease, "Don't double-acquire a lease");
            _ownLease = true;
            _blob = blob;

            var blobLock = GetBlobSuffix(_blob, ".lease");
            Assert.IsFalse(BlobClient.DoesBlobExist(blobLock), "Somebody else has the lease");
            blobLock.UploadText("held");
        }

        public IBlobLeaseHolder TransferOwnership()
        {
            Assert.IsTrue(_ownLease);
            _ownLease = false;
            // blob.lease still exists, so blob is still leased. 

            return new MockBlobLeaseHolder { 
                 _blob = _blob,
                _ownLease = true,                
            };
        }

        public void UploadText(string text)
        {
            Assert.IsTrue(_ownLease);
            _blob.UploadText(text);

            // Write to a second blob to prove that we wrote while holding the lease. 
            var blob2 = GetBlobSuffix(_blob, ".x");
            blob2.UploadText(text);


            var blobLock = GetBlobSuffix(_blob, ".lease");            
            Assert.IsTrue(BlobClient.DoesBlobExist(blobLock), "Writing without a lease");
        }

        public void Dispose()
        {
            if (_ownLease)
            {
                var blobLock = GetBlobSuffix(_blob, ".lease");
                blobLock.Delete();
            }
            _ownLease = false;
        }
    }
}
