using System;
using System.IO;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk2
{
    // Test model binding with Azure 2.0 sdk 
    public class UnitTest1
    {
        // Test binding a parameter to the CloudStorageAccount that a function is uploaded to. 
        [Fact]
        public void TestBindCloudStorageAccount()
        {
            var lc = new TestJobHost<Program>();
            lc.Call("FuncCloudStorageAccount");
        }

        [Fact]
        public void TestIBlob()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference("daas-test");
            container.CreateIfNotExists();

            var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            var block = container.GetBlockBlobReference("block");
            block.UploadFromStream(stream);

            var page = container.GetPageBlobReference("page");
            page.UploadFromStream(stream);

            var lc = new TestJobHost<Program>();

            lc.Call("IBlob");

            lc.Call("BlockBlob");

            lc.Call("PageBlob");

            container.DeleteIfExists();
        }

        [Fact]
        public void TestMissingIBlob()
        {
            var lc = new TestJobHost<Program>();
            lc.Call("IBlobMissing");
            lc.Call("BlockBlobMissing");
            lc.Call("PageBlobMissing");
        }

        [Fact]
        public void TestQueue()
        {
            var lc = new TestJobHost<Program>();
            lc.Call("Queue");
            Assert.True(Program._QueueInvoked);
        }

        [Fact]        
        public void TestQueueBadName()
        {
            // indexer should notice bad queue name and fail immediately
            Assert.Throws<IndexException>(() => new TestJobHost<ProgramBadQueueName>(null));
        }

        [Fact]
        public void TestTable()
        {
            var lc = new TestJobHost<Program>();
            lc.Call("Table");
            Assert.True(Program.TableInvoked);
        }

        class ProgramBadQueueName
        {
            [Description("test")]
            public static void QueueBadName(CloudQueue IllegalName)
            {
                throw new NotSupportedException("shouldnt get invoked");
            }
        }

        class Program
        {
            // Test binding to CloudStorageAccount 
            [Description("test")]
            public static void FuncCloudStorageAccount(CloudStorageAccount account)
            {
                var account2 = CloudStorageAccount.DevelopmentStorageAccount;

                Assert.Equal(account.ToString(), account2.ToString());
            }

            public static bool _QueueInvoked;

            public static bool TableInvoked { get; set; }

            [Description("test")]
            public static void Queue(CloudQueue mytestqueue)
            {
                _QueueInvoked = true;
                Assert.NotNull(mytestqueue);
            }

            public static void IBlobMissing(
                [BlobInput("daas-test/missing")] ICloudBlob missing)
            {
                Assert.Null(missing);
            }

            public static void IBlob(
                [BlobInput("daas-test/page")] ICloudBlob page,
                [BlobInput("daas-test/block")] ICloudBlob block)
            {
                Assert.NotNull(page);
                Assert.NotNull(block);

                Assert.Equal(BlobType.PageBlob, page.BlobType);
                Assert.Equal(BlobType.BlockBlob, block.BlobType);
            }

            public static void BlockBlob(
               [BlobInput("daas-test/block")] CloudBlockBlob block)
            {
                Assert.Equal(BlobType.BlockBlob, block.BlobType);
            }

            public static void PageBlob(
                [BlobInput("daas-test/page")] CloudPageBlob page)
            {
                Assert.Equal(BlobType.PageBlob, page.BlobType);
            }


            public static void BlockBlobMissing(
               [BlobInput("daas-test/missing")] CloudBlockBlob block)
            {
                Assert.Null(block);
            }

            public static void PageBlobMissing(
                [BlobInput("daas-test/page")] CloudPageBlob page)
            {
                Assert.Null(page);
            }

            [Description("test")]
            public static void Table([Table("DaasTestTable")] CloudTable table)
            {
                Assert.NotNull(table);
                TableInvoked = true;
            }
        }
    }
}
