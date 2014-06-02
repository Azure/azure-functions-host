using System;
using System.Configuration;
using System.Threading;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.EndToEndTests
{
    /// <summary>
    /// Tests for the lease expiration tests
    /// </summary>
    public class LeaseExpirationTests
    {
        private const string TestQueueName = "queueleaserenew";

        private static bool _messageFoundAgain;

        private static CancellationTokenSource _tokenSource;

        private string _connectionString;

        private CloudStorageAccount _storageAccount;

        /// <summary>
        /// Initializes a new instance of the <see cref="LeaseExpirationTests"/> class.
        /// </summary>
        public LeaseExpirationTests()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["Test"].ConnectionString;

            try
            {
                _storageAccount = CloudStorageAccount.Parse(_connectionString);
            }
            catch (Exception ex)
            {
                throw new FormatException("The connection string in App.config is invalid", ex);
            }

            // Make sure there are no messages in the queue before running the test
            CloudQueueClient queueClient = _storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference(TestQueueName);
            if (queue.Exists())
            {
                queue.Clear();
            }

            _tokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// The function used to test the renewal. Whenever it is invoked, a counter increases
        /// </summary>
        public static void QueueMessageLeaseRenewFunction([QueueTrigger(TestQueueName)] string message, CloudQueue queueleaserenew)
        {
            // The time when the function will stop if the message is not found again
            DateTime endTime = DateTime.Now.AddMinutes(15);

            while (DateTime.Now < endTime)
            {
                CloudQueueMessage queueMessage = queueleaserenew.GetMessage();
                if (queueMessage != null)
                {
                    _messageFoundAgain = true;
                    break;
                }

                Thread.Sleep(TimeSpan.FromSeconds(30));
            }

            _tokenSource.Cancel();
        }

        /// <summary>
        /// There is a function that takes > 10 minutes and listens to a queue.
        /// </summary>
        /// <remarks>Ignored because it takes a long time. Can be enabled on demand</remarks>
        // Switch the Fact attribute to run
        [Fact(Skip = "Slow test with 20 minutes timeout")]
        //[Fact(Timeout = 20 * 60 * 1000)]
        public void QueueMessageLeaseRenew()
        {
            _messageFoundAgain = false;

            JobHost host = new JobHost(new JobHostConfiguration(_connectionString));

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            CloudQueueClient queueClient = _storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference(TestQueueName);
            queue.CreateIfNotExists();
            queue.AddMessage(new CloudQueueMessage("Test"));

            host.RunAndBlock(_tokenSource.Token);

            Assert.False(_messageFoundAgain);
        }
    }
}
