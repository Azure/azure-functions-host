// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Protocols
{
    /// <summary>Represents a host message sender.</summary>
    public class HostMessageSender : IHostMessageSender
    {
        private readonly CloudQueueClient _client;

        /// <summary>Initializes a new instance of the <see cref="HostMessageSender"/> class.</summary>
        /// <param name="client">A queue client for the storage account where the host listens.</param>
        [CLSCompliant(false)]
        public HostMessageSender(CloudQueueClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            _client = client;
        }

        /// <inheritdoc />
        public void Enqueue(string queueName, HostMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            CloudQueue queue = _client.GetQueueReference(queueName);
            Debug.Assert(queue != null);
            queue.CreateIfNotExists();
            string content = JsonConvert.SerializeObject(message, JsonSerialization.Settings);
            Debug.Assert(content != null);
            CloudQueueMessage queueMessage = new CloudQueueMessage(content);
            queue.AddMessage(queueMessage);
        }
    }
}
