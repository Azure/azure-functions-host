using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Azure20SdkUnitTests
{
    // Test model binding with Azure 2.0 sdk 
    [TestClass]
    public class UnitTest1
    {
        // Test binding a parameter to the CloudStorageAccount that a function is uploaded to. 
        [TestMethod]
        public void TestBindCloudStorageAccount()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;                     
            var acs = account.ToString(true);

            var lc = new LocalExecutionContext(acs, typeof(Program));
            lc.Call("FuncCloudStorageAccount");
        }

        [TestMethod]
        public void TestIBlob()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;
            var acs = account.ToString(true);

            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference("daas-test");
            container.CreateIfNotExists();

            var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            var block = container.GetBlockBlobReference("block");            
            block.UploadFromStream(stream);

            var page = container.GetPageBlobReference("page");
            page.UploadFromStream(stream);
            
            var lc = new LocalExecutionContext(acs, typeof(Program));
            lc.Call("IBlob");

            lc.Call("BlockBlob");

            lc.Call("PageBlob");

            container.DeleteIfExists();
        }

        [TestMethod]
        public void TestMissingIBlob()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;
            var acs = account.ToString(true);

            var lc = new LocalExecutionContext(acs, typeof(Program));
            lc.Call("IBlobMissing");
            lc.Call("BlockBlobMissing");
            lc.Call("PageBlobMissing");
        }

        [TestMethod]
        public void TestQueue()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;
            var acs = account.ToString(true);

            var lc = new LocalExecutionContext(acs, typeof(Program));
            lc.Call("Queue");
            Assert.IsTrue(Program._QueueInvoked);
        }
        
        //[TestMethod]
        // TODO: ammend this!
        public void TestQueueBadName()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;
            var acs = account.ToString(true);

            var lc = new LocalExecutionContext(acs, typeof(Program));

            try
            {
                lc.Call("QueueBadName");
                Assert.Fail("indexer should have noticed bad queue name and failed immediately");
            }
            catch (IndexException)
            {
            }
        }

        class Program
        {
            // Test binding to CloudStorageAccount 
            [Microsoft.WindowsAzure.Jobs.Description("test")]
            public static void FuncCloudStorageAccount(CloudStorageAccount account)
            {                
                var account2 = CloudStorageAccount.DevelopmentStorageAccount;

                Assert.AreEqual(account.ToString(), account2.ToString());
            }

            public static bool _QueueInvoked;

            [Microsoft.WindowsAzure.Jobs.Description("test")]
            public static void Queue(CloudQueue mytestqueue)
            {
                _QueueInvoked = true;
                Assert.IsNotNull(mytestqueue);
            }

            [Microsoft.WindowsAzure.Jobs.Description("test")]
            public static void QueueBadName(CloudQueue IllegalName)
            {
                Assert.Fail("shouldnt get invoked");
            }

            public static void IBlobMissing(
                [BlobInput("daas-test/missing")] ICloudBlob missing)
            {
                Assert.IsNull(missing);
            }

            public static void IBlob(
                [BlobInput("daas-test/page")] ICloudBlob page,
                [BlobInput("daas-test/block")] ICloudBlob block)
            {
                Assert.IsNotNull(page);
                Assert.IsNotNull(block);

                Assert.AreEqual(BlobType.PageBlob, page.BlobType);
                Assert.AreEqual(BlobType.BlockBlob, block.BlobType);
            }

            public static void BlockBlob(
               [BlobInput("daas-test/block")] CloudBlockBlob block)
            {
                Assert.AreEqual(BlobType.BlockBlob, block.BlobType);
            }

            public static void PageBlob(
                [BlobInput("daas-test/page")] CloudPageBlob page)
            {                
                Assert.AreEqual(BlobType.PageBlob, page.BlobType);
            }


            public static void BlockBlobMissing(
               [BlobInput("daas-test/missing")] CloudBlockBlob block)
            {
                Assert.IsNull(block);
            }

            public static void PageBlobMissing(
                [BlobInput("daas-test/page")] CloudPageBlob page)
            {
                Assert.IsNull(page);
            }
        }
    }
}
