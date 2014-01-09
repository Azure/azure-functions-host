using System;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.WindowsAzure.Jobs
{
    // This tracks causality via the queue message payload. 
    // Important that this can interoperate with external queue messages, so be resilient to a missing guid marker. 
    // Can we switch to some auxillary table? Beware, CloudQueueMessage.Id is not 
    // filled out until after the message is queued, but then there's a race between updating 
    // the aux storage and another function picking up the message.
    internal class QueueCausalityHelper
    {
        // Serialize Payloads as JSON. Add an extra field to the JSON object for the parent guid name.
        const string parentGuidFieldName = "$AzureJobsParentId";

        // When we enqueue, add the 
        public CloudQueueMessage EncodePayload(Guid functionOwner, object payload)
        {
            JToken token = JToken.FromObject(payload);
            token[parentGuidFieldName] = functionOwner.ToString();

            string json = token.ToString();
            
            CloudQueueMessage msg = new CloudQueueMessage(json);
            // Beware, msg.id is not filled out yet. 
            return msg;
        }

        public Guid GetOwner(CloudQueueMessage msg)
        {
            string json = msg.AsString;

            try
            {
                JToken token = JToken.Parse(json);
                string val = (string)token[parentGuidFieldName];

                Guid guid;
                Guid.TryParse(val, out guid);

                return guid;
            }
            catch (JsonReaderException)
            {
                return Guid.Empty;
            }
            catch (InvalidOperationException)
            {
                return Guid.Empty;
            }
        }
    }
}
