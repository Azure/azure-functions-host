using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
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
            TestBlobClient.DeleteContainer(account, containerName);

            CloudBlobClient client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(containerName);
            IBlobListener l = new ContainerScannerBlobListener(new CloudBlobContainer[] { container });
            RuntimeBindingProviderContext context = new RuntimeBindingProviderContext
            {
                CancellationToken = CancellationToken.None
            };

            l.Poll((blob, ignore) =>
                {
                    Assert.True(false, "shouldn't be any blobs in the container");
                }, context);

            TestBlobClient.WriteBlob(account, containerName, "foo1.csv", "abc");

            int count = 0;
            l.Poll((blob, ignore) =>
            {
                count++;
                Assert.Equal("foo1.csv", blob.Name);
            }, context);
            Assert.Equal(1, count);

            // No poll again, shouldn't show up. 
            l.Poll((blob, ignore) =>
            {
                Assert.True(false, "shouldn't retrigger the same blob");
            }, context);            
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
