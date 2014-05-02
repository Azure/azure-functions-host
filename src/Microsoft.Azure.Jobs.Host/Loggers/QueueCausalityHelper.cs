using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.Jobs
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

        [DebuggerNonUserCode]
        public Guid GetOwner(CloudQueueMessage msg)
        {
            string json = msg.AsString;
            IDictionary<string, JToken> jsonObject;
            try
            {
                jsonObject = JObject.Parse(json);
            }
            catch (Exception)
            {
                return Guid.Empty;
            }

            if (!jsonObject.ContainsKey(parentGuidFieldName) || jsonObject[parentGuidFieldName].Type != JTokenType.String)
            {
                return Guid.Empty;
            }

            string val = (string)jsonObject[parentGuidFieldName];

            Guid guid;
            Guid.TryParse(val, out guid);
            return guid;
        }
    }
}
