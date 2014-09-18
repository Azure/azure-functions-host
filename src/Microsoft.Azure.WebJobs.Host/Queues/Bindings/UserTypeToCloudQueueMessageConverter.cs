// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class UserTypeToCloudQueueMessageConverter<TInput> : IConverter<TInput, CloudQueueMessage>
    {
        private readonly Guid _functionInstanceId;

        public UserTypeToCloudQueueMessageConverter(Guid functionInstanceId)
        {
            _functionInstanceId = functionInstanceId;
        }

        public CloudQueueMessage Convert(TInput input)
        {
            JToken token;

            if (input == null)
            {
                token = new JValue((string)null);
            }
            else
            {
                JObject objectToken = JObject.FromObject(input, JsonSerialization.Serializer);
                Debug.Assert(objectToken != null);
                QueueCausalityManager.SetOwner(_functionInstanceId, objectToken);
                token = objectToken;
            }

            string contents = token.ToJsonString();
            return new CloudQueueMessage(contents);
        }
    }
}
