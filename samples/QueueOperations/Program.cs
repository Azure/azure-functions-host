using System;
using System.Configuration;
using System.IO;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;

namespace QueueOperations
{
    class Order
    {
        public string Name { get; set; }

        public string OrderId { get; set; }
    }

    class Program
    {
        /// <summary>
        /// Reads an Order object from the "initialorder" queue
        /// Creates a blob for the specified order which contains the order details
        /// The message in "orders" will be picked up by "QueueToBlob"
        /// </summary>
        public static void MultipleOutput([QueueInput("initialorder")] Order order, [BlobOutput("orders/{OrderId}")] out string orderBlob, [QueueOutput] out string orders)
        {
            orderBlob = order.OrderId;
            orders = order.OrderId;
        }

        /// <summary>
        /// Reads a message from the "orders" queue and writes a blob in the "orders" container
        /// </summary>
        public static void QueueToBlob([QueueInput] string orders, IBinder binder)
        {
            TextWriter writer = binder.Bind<TextWriter>(new BlobOutputAttribute("orders/" + orders));
            writer.Write("Completed");
        }

        /// <summary>
        /// Shows binding parameters to properties of queue messages
        /// 
        /// The "Name" parameter will get the value of the "Name" property in the Order object
        /// </summary>
        public static void PropertyBinding([QueueInput] Order initialorder, string Name)
        {
            Console.WriteLine("New order from: {0}", Name);
        }

        static void Main()
        {
            CreateDemoData();

            JobHost host = new JobHost();
            host.RunAndBlock();
        }

        private static void CreateDemoData()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.ConnectionStrings["AzureJobsData"].ConnectionString);

            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference("initialorder");
            queue.CreateIfNotExist();

            Order person = new Order()
            {
                Name = "Alex",
                OrderId = Guid.NewGuid().ToString("N").ToLower()
            };

            queue.AddMessage(new CloudQueueMessage(JsonConvert.SerializeObject(person)));
        }
    }
}
