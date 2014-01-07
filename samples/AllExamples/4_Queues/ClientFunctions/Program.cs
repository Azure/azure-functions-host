using Microsoft.WindowsAzure; // Nuget package: WindowsAzure.Storage
using Microsoft.WindowsAzure.StorageClient; // Nuget package: WindowsAzure.Storage
using Newtonsoft.Json; // Nuget package: Newtonsoft.Json

// Note the client does not need a reference to SimpleBatch.dll. 
// It is just communicating with Azure queues. 

class Program
{
    // This shows how a client can queue a message that SimpleBatch will read with [QueueInput]
    static void Main(string[] args)
    {
        // Payload is the same object that we'll get on the server side. 
        Payload payload = new Payload { Name = "Client", Value = 123 };

        EnqueueToSimpleBatch(payload, queueName: "myTestQueue");
    }

    static void EnqueueToSimpleBatch(object payload, string queueName)
    {
        // SimpleBatch Queue payloads are serialized as JSON. Use JSON.Net to serialize.
        var json = JsonConvert.SerializeObject(payload);
        CloudQueueMessage msg = new CloudQueueMessage(json);

        CloudQueue queue = GetQueue(queueName);
        queue.AddMessage(msg);
    }

    static CloudQueue GetQueue(string queueName)
    {
        string accountConnectionString = "<your string here!>";
        CloudStorageAccount account = CloudStorageAccount.Parse(accountConnectionString);
        CloudQueueClient client = account.CreateCloudQueueClient();        
        CloudQueue queue = client.GetQueueReference(queueName);
        queue.CreateIfNotExist();

        return queue;
    }
}
