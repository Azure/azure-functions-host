// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class BlobQueueTriggerExecutor : ITriggerExecutor<IStorageQueueMessage>
    {
        private readonly CloudBlobClient _client;
        private readonly IBlobETagReader _eTagReader;
        private readonly IBlobCausalityReader _causalityReader;
        private readonly IFunctionExecutor _innerExecutor;
        private readonly IBlobWrittenWatcher _blobWrittenWatcher;
        private readonly ConcurrentDictionary<string, ITriggeredFunctionInstanceFactory<ICloudBlob>> _registrations;

        public BlobQueueTriggerExecutor(CloudBlobClient client, IFunctionExecutor innerExecutor,
            IBlobWrittenWatcher blobWrittenWatcher)
            : this(client, BlobETagReader.Instance, BlobCausalityReader.Instance, innerExecutor, blobWrittenWatcher)
        {
        }

        public BlobQueueTriggerExecutor(CloudBlobClient client, IBlobETagReader eTagReader,
            IBlobCausalityReader causalityReader, IFunctionExecutor innerExecutor,
            IBlobWrittenWatcher blobWrittenWatcher)
        {
            _client = client;
            _eTagReader = eTagReader;
            _causalityReader = causalityReader;
            _innerExecutor = innerExecutor;
            _blobWrittenWatcher = blobWrittenWatcher;
            _registrations = new ConcurrentDictionary<string, ITriggeredFunctionInstanceFactory<ICloudBlob>>();
        }

        public void Register(string functionId, ITriggeredFunctionInstanceFactory<ICloudBlob> instanceFactory)
        {
            _registrations.AddOrUpdate(functionId, instanceFactory, (i1, i2) => instanceFactory);
        }

        public async Task<bool> ExecuteAsync(IStorageQueueMessage value, CancellationToken cancellationToken)
        {
            BlobTriggerMessage message = JsonConvert.DeserializeObject<BlobTriggerMessage>(value.AsString,
                JsonSerialization.Settings);

            if (message == null)
            {
                throw new InvalidOperationException("Invalid blob trigger message.");
            }

            string functionId = message.FunctionId;

            if (functionId == null)
            {
                throw new InvalidOperationException("Invalid function ID.");
            }

            // Ensure that the function ID is still valid. Otherwise, ignore this message.
            ITriggeredFunctionInstanceFactory<ICloudBlob> instanceFactory;

            if (!_registrations.TryGetValue(functionId, out instanceFactory))
            {
                return true;
            }

            CloudBlobContainer container = _client.GetContainerReference(message.ContainerName);
            string blobName = message.BlobName;

            ICloudBlob blob;
            
            switch (message.BlobType)
            {
                case BlobType.PageBlob:
                    blob = container.GetPageBlobReference(blobName);
                    break;
                case BlobType.BlockBlob:
                default:
                    blob = container.GetBlockBlobReference(blobName);
                    break;
            }

            // Ensure the blob still exists with the same ETag.
            string possibleETag = await _eTagReader.GetETagAsync(blob, cancellationToken);

            if (possibleETag == null)
            {
                // If the blob no longer exists, just ignore this message.
                return true;
            }

            // If the blob still exists but the ETag is different, delete the message but do a fast path notification.
            if (!String.Equals(message.ETag, possibleETag, StringComparison.Ordinal))
            {
                _blobWrittenWatcher.Notify(blob);
                return true;
            }

            //// If the blob still exists and its ETag is still valid, execute.
            //// Note: it's possible the blob could change/be deleted between now and when the function executes.
            Guid? parentId = await _causalityReader.GetWriterAsync(blob, cancellationToken);
            IFunctionInstance instance = instanceFactory.Create(blob, parentId);
            IDelayedException exception = await _innerExecutor.TryExecuteAsync(instance, cancellationToken);
            return exception == null;
        }
    }
}
