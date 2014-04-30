using Microsoft.WindowsAzure.Storage.Blob;
using System.Text;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    // $$$ Get more aggressive testing here. 
    // We can Read a blob without a lease. 
    class MockBlobLeaseHolder : IBlobLeaseHolder
    {
        bool _ownLease;
        ICloudBlob _blob;

        public static ICloudBlob GetBlobSuffix(ICloudBlob blob, string suffix)
        {
            var container = blob.Container;
            string name = blob.Name + suffix;
            ICloudBlob blob2 = container.GetBlobReferenceFromServer(name);
            return blob2;
        }

        public void BlockUntilAcquired(ICloudBlob blob)
        {
            Assert.False(_ownLease, "Don't double-acquire a lease");
            _ownLease = true;
            _blob = blob;

            var blobLock = GetBlobSuffix(_blob, ".lease");
            Assert.False(BlobClient.DoesBlobExist(blobLock), "Somebody else has the lease");
            UploadText(blobLock, "held");
        }

        public IBlobLeaseHolder TransferOwnership()
        {
            Assert.True(_ownLease);
            _ownLease = false;
            // blob.lease still exists, so blob is still leased. 

            return new MockBlobLeaseHolder { 
                 _blob = _blob,
                _ownLease = true,                
            };
        }

        public void UploadText(string text)
        {
            Assert.True(_ownLease);
            UploadText(_blob, text);

            // Write to a second blob to prove that we wrote while holding the lease. 
            var blob2 = GetBlobSuffix(_blob, ".x");
            UploadText(blob2, text);


            var blobLock = GetBlobSuffix(_blob, ".lease");            
            Assert.True(BlobClient.DoesBlobExist(blobLock), "Writing without a lease");
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

        private static void UploadText(ICloudBlob blob, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            blob.UploadFromByteArray(bytes, 0, bytes.Length);
        }
    }
}
