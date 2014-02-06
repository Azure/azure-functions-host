using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class Invoker : IInvoker
    {
        private readonly CloudStorageAccount _account;

        public Invoker(CloudStorageAccount account)
        {
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }

            _account = account;
        }

        public void Invoke(Guid hostId, InvocationMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            CloudQueueClient client = _account.CreateCloudQueueClient();
            Debug.Assert(client != null);
            CloudQueue queue = client.GetQueueReference(EndpointNames.GetInvokeQueueName(hostId));
            Debug.Assert(queue != null);
            string content = JsonCustom.SerializeObject(message);
            Debug.Assert(content != null);
            queue.CreateIfNotExist();
            queue.AddMessage(new CloudQueueMessage(content));
        }
    }
}
