using System;
using System.Collections.Generic;
using System.Diagnostics;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using SimpleBatch;

namespace RunnerInterfaces
{
    // This tracks causality via the queue message payload. 
    // $$$ This is bad because it means we can't interop queue sources with extenals. 
    // Can we switch to some auxillary table? Beware, CloudQueueMessage.Id is not 
    // filled out until after the message is queued, but then there's a race between updating 
    // the aux storage and another function picking up the message.
    public class QueueCausalityHelper
    {
        // When we enqueue, add the 
        public CloudQueueMessage EncodePayload(Guid functionOwner, object payload)
        {
            string json = JsonCustom.SerializeObject(payload);

            string x = functionOwner.ToString() + "," + json;

            CloudQueueMessage msg = new CloudQueueMessage(x);
            // Beware, msg.id is not filled out yet. 
            return msg;
        }

        public string DecodePayload(CloudQueueMessage msg)
        {
            string x = msg.AsString;
            int i = x.IndexOf(',');
            string payload = x.Substring(i + 1);
            return payload;
        }

        public Guid GetOwner(CloudQueueMessage msg)
        {
            string x = msg.AsString;
            int i = x.IndexOf(',');
            string owner = x.Substring(0, i);

            Guid guid;
            Guid.TryParse(owner, out guid);

            return guid;
        }
    }
}