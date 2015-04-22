// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Queue;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Queue
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Queue
#endif
{
    /// <summary>Represents a queue client.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageQueueClient : IStorageQueueClient
#else
    internal class StorageQueueClient : IStorageQueueClient
#endif
    {
        private readonly CloudQueueClient _sdk;

        /// <summary>Initializes a new instance of the <see cref="StorageQueueClient"/> class.</summary>
        /// <param name="sdk">The SDK client to wrap.</param>
        public StorageQueueClient(CloudQueueClient sdk)
        {
            _sdk = sdk;
        }

        /// <inheritdoc />
        public StorageCredentials Credentials
        {
            get { return _sdk.Credentials; }
        }

        /// <inheritdoc />
        public IStorageQueue GetQueueReference(string queueName)
        {
            CloudQueue sdkQueue = _sdk.GetQueueReference(queueName);
            return new StorageQueue(this, sdkQueue);
        }
    }
}
