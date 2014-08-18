// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Host.Queues
{
    // This tracks causality via the queue message payload. 
    // Important that this can interoperate with external queue messages, so be resilient to a missing guid marker. 
    // Can we switch to some auxillary table? Beware, CloudQueueMessage.Id is not 
    // filled out until after the message is queued, but then there's a race between updating 
    // the aux storage and another function picking up the message.
    internal static class QueueCausalityManager
    {
        // Serialize Payloads as JSON. Add an extra field to the JSON object for the parent guid name.
        const string parentGuidFieldName = "$AzureWebJobsParentId";

        // When we enqueue, add the 
        public static CloudQueueMessage EncodePayload(Guid functionOwner, object payload)
        {
            JToken token = JToken.FromObject(payload);
            token[parentGuidFieldName] = functionOwner.ToString();

            string json = token.ToString();

            CloudQueueMessage msg = new CloudQueueMessage(json);
            // Beware, msg.id is not filled out yet. 
            return msg;
        }

        [DebuggerNonUserCode]
        public static Guid? GetOwner(CloudQueueMessage msg)
        {
            string json = msg.AsString;
            IDictionary<string, JToken> jsonObject;
            try
            {
                jsonObject = JObject.Parse(json);
            }
            catch (Exception)
            {
                return null;
            }

            if (!jsonObject.ContainsKey(parentGuidFieldName) || jsonObject[parentGuidFieldName].Type != JTokenType.String)
            {
                return null;
            }

            string val = (string)jsonObject[parentGuidFieldName];

            Guid guid;
            if (Guid.TryParse(val, out guid))
            {
                return guid;
            }
            return null;
        }
    }
}
