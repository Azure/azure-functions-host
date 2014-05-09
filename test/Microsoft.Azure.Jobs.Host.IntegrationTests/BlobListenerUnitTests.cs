using System.Threading;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    public class BlobListenerTests
    {
        [Fact]
        public void TestBlobListener()
        {
            var account = TestStorage.GetAccount();
            string containerName = @"daas-test-input";
            BlobClient.DeleteContainer(account, containerName);

            CloudBlobClient client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(containerName);
            IBlobListener l = new ContainerScannerBlobListener(new CloudBlobContainer[] { container });

            l.Poll((blob, cancel) =>
                {
                    Assert.True(false, "shouldn't be any blobs in the container");
                }, CancellationToken.None);

            BlobClient.WriteBlob(account, containerName, "foo1.csv", "abc");

            int count = 0;
            l.Poll((blob, cancel) =>
            {
                count++;
                Assert.Equal("foo1.csv", blob.Name);
            }, CancellationToken.None);
            Assert.Equal(1, count);

            // No poll again, shouldn't show up. 
            l.Poll((blob, cancel) =>
            {
                Assert.True(false, "shouldn't retrigger the same blob");
            }, CancellationToken.None);            
        }

        // Set dev storage. These are well known values.
        class TestStorage
        {
            public static CloudStorageAccount GetAccount()
            {
                return CloudStorageAccount.DevelopmentStorageAccount;
            }
        }
    }
}
