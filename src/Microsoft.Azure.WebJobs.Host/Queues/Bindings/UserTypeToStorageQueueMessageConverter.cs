// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class UserTypeToStorageQueueMessageConverter<TInput> : IConverter<TInput, IStorageQueueMessage>
    {
        private readonly IStorageQueue _queue;
        private readonly Guid _functionInstanceId;

        public UserTypeToStorageQueueMessageConverter(IStorageQueue queue, Guid functionInstanceId)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            _queue = queue;
            _functionInstanceId = functionInstanceId;
        }

        public IStorageQueueMessage Convert(TInput input)
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
            return _queue.CreateMessage(contents);
        }
    }
}
