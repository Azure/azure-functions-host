using System.Configuration;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;

namespace HelloWorld
{
    class Program
    {
        /// <summary>
        /// Reads a message as string for the queue named "inputtext"
        /// Outputs the text in the blob helloworld/out.txt
        /// </summary>
        public static void HelloWorldFunction([QueueInput] string inputText, [BlobOutput("helloworld/out.txt")] out string output)
        {
            output = inputText;
        }

        static void Main()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.ConnectionStrings["AzureJobsData"].ConnectionString);
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference("inputtext");
            queue.CreateIfNotExist();
            queue.AddMessage(new CloudQueueMessage("Hello World!"));

            // The connection string is read from App.config
            JobHost host = new JobHost();
            host.RunAndBlock();
        }
    }
}
