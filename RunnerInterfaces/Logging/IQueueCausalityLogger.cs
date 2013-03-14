using System;
using System.Collections.Generic;
using System.Diagnostics;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using SimpleBatch;

namespace RunnerInterfaces
{
    // !!! This tracks causality via the queue message payload. 
    // This is bad because it means we can't interop queue sources with extenals. 
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

#if false
    public interface IQueueCausalityLogger
    {
        void SetWriter(string messageId, Guid function);

        Guid GetWriter(string messageId);

        // !!! Queue Messages are transient (unlike blobs). So we could remove the writer once we're done. 
    }

    public class QueueCausalityPayload
    {
        public Guid Function { get; set; }
    }

    public class QueueCausalityLogger : IQueueCausalityLogger
    {
        IAzureTable<QueueCausalityPayload> _table;

        public void SetWriter(string messageId, Guid function)
        {
            string partKey = "1";
            string rowKey = messageId;

            QueueCausalityPayload values = new QueueCausalityPayload 
            {
                Function = function
            };

            _table.Write(partKey, rowKey, values);

            // !!! We may queue up many messgaes, so we can we batch the writers too?
            _table.Flush();
        }

        public Guid GetWriter(string messageId)
        {
            string partKey = "1";
            string rowKey = messageId;
            var values = _table.Lookup(partKey, rowKey);

            if (values == null)
            {
                return Guid.Empty;
            }
            return values.Function;
        }
    }
#endif
}