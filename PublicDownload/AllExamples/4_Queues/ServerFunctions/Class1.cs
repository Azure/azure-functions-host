using System;
using System.Collections.Generic;
using SimpleBatch;

// Queues messages get serialized/deserialized to a Payload using JSON. 
public class Payload
{
    public string Name { get; set ;}
    public int Value { get; set; }
}

public class QueueTest
{
    // NoAutomaticTrigger means that this function does not get automatically triggered by the orchestrator.
    // Instead, invoke it explicitly through ICall or the web dashboard. 
    // The QueueOutput attribute means that if the output parameter is not null, then convert it to a queue message and 
    // add it to the queue. The queue name is the parameter name (eg, "myTestQueue") 
    // Name and value unbound values that are provided when you invoke the function 
    [NoAutomaticTrigger]
    public static void EnqueueManually([QueueOutput] out Payload myTestQueue, string name, int value)
    {
        // Enqueues a single message.
        myTestQueue = new Payload
        {
            Name = name,
            Value = value
        };
    }

    // shows how to enqueue multiple messages. The output parameter needs to be exactly IEnumerable<T> 
    [NoAutomaticTrigger]
    public static void EnqueueMultiple([QueueOutput] out IEnumerable<Payload> myTestQueue)
    {
        myTestQueue = EnqueueMultiple(); 
    }
    private static IEnumerable<Payload> EnqueueMultiple()
    {
        yield return new Payload { Name = "First", Value = 100 };
        yield return new Payload { Name = "Second", Value = 200 };
        yield return new Payload { Name = "Third", Value = 300 };
    }

#if false
    // You could also bind directly to Microsoft.WindowsAzure.StorageClient.CloudQueue
    // This is commented out since we don't have a reference to that. 
    [Description("cloud queue function")] // needed for indexing since we have no other attrs
    public static void EnqueueDirect(CloudQueue myTestQueue)
    {
        CloudQueueMessage message = new CloudQueueMessage(...);
        myTestQueue.AddMessage(message); // or call other overloads
    }
#endif
        
    // this function is invoked when a queue message becomes avaialable on queue "myTestQueue".
    public static void Dequeue([QueueInput] Payload myTestQueue)
    {
        Console.WriteLine("{0},{1}", myTestQueue.Name, myTestQueue.Value);
    }
}

