using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.Jobs
{
    // Base class for triggers that client can listen on. 
    internal abstract class Trigger
    {
        // Invoke this path when the trigger fires 
        public string CallbackPath { get; set; }

        // Not serialized. For in-memory cases.(This is kind of exclusive with CallbackPath)
        public object Tag { get; set; }

        // $$$ Need abstraction here, may get via Azure Web Sites instead. 
        public string StorageConnectionString { get; set; }

        public TriggerType Type { get; set; }

        public static Trigger FromWire(TriggerRaw raw, Credentials credentials)
        {
            switch (raw.Type)
            {
                case TriggerType.Blob:
                    var trigger = new BlobTrigger
                    {
                        StorageConnectionString = credentials.StorageConnectionString,
                        BlobInput = new CloudBlobPath(raw.BlobInput)
                    };
                    if (raw.BlobOutput != null)
                    {
                        string[] parts = raw.BlobOutput.Split(';');
                        trigger.BlobOutputs = Array.ConvertAll(parts, part => new CloudBlobPath(part.Trim()));
                    }
                    return trigger;
                case TriggerType.Queue:
                    return new QueueTrigger
                    {
                        StorageConnectionString = credentials.StorageConnectionString,
                        QueueName = raw.QueueName
                    };
                case TriggerType.ServiceBus:
                    return new ServiceBusTrigger
                    {
                        StorageConnectionString = credentials.ServiceBusConnectionString,
                        SourcePath = raw.EntityName
                    };
                default:
                    throw new InvalidOperationException("Unknown Trigger type:" + raw.Type);
            }
        }
    }
}
