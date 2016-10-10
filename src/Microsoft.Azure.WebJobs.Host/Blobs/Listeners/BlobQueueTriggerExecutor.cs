// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class BlobQueueTriggerExecutor : ITriggerExecutor<IStorageQueueMessage>
    {
        private readonly IBlobETagReader _eTagReader;
        private readonly IBlobCausalityReader _causalityReader;
        private readonly IBlobWrittenWatcher _blobWrittenWatcher;
        private readonly ConcurrentDictionary<string, BlobQueueRegistration> _registrations;

        public BlobQueueTriggerExecutor(IBlobWrittenWatcher blobWrittenWatcher)
            : this(BlobETagReader.Instance, BlobCausalityReader.Instance, blobWrittenWatcher)
        {
        }

        public BlobQueueTriggerExecutor(IBlobETagReader eTagReader,
            IBlobCausalityReader causalityReader, IBlobWrittenWatcher blobWrittenWatcher)
        {
            _eTagReader = eTagReader;
            _causalityReader = causalityReader;
            _blobWrittenWatcher = blobWrittenWatcher;
            _registrations = new ConcurrentDictionary<string, BlobQueueRegistration>();
        }

        public void Register(string functionId, BlobQueueRegistration registration)
        {
            _registrations.AddOrUpdate(functionId, registration, (i1, i2) => registration);
        }

        public async Task<FunctionResult> ExecuteAsync(IStorageQueueMessage value, CancellationToken cancellationToken)
        {
            BlobTriggerMessage message = JsonConvert.DeserializeObject<BlobTriggerMessage>(value.AsString, JsonSerialization.Settings);

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
            FunctionResult successResult = new FunctionResult(true);
            BlobQueueRegistration registration;
            if (!_registrations.TryGetValue(functionId, out registration))
            {
                return successResult;
            }

            IStorageBlobContainer container = registration.BlobClient.GetContainerReference(message.ContainerName);
            string blobName = message.BlobName;

            IStorageBlob blob;

            switch (message.BlobType)
            {
                case StorageBlobType.PageBlob:
                    blob = container.GetPageBlobReference(blobName);
                    break;
                case StorageBlobType.AppendBlob:
                    blob = container.GetAppendBlobReference(blobName);
                    break;
                case StorageBlobType.BlockBlob:
                default:
                    blob = container.GetBlockBlobReference(blobName);
                    break;
            }

            // Ensure the blob still exists with the same ETag.
            string possibleETag = await _eTagReader.GetETagAsync(blob, cancellationToken);

            if (possibleETag == null)
            {
                // If the blob no longer exists, just ignore this message.
                return successResult;
            }

            // If the blob still exists but the ETag is different, delete the message but do a fast path notification.
            if (!String.Equals(message.ETag, possibleETag, StringComparison.Ordinal))
            {
                _blobWrittenWatcher.Notify(blob);
                return successResult;
            }

            // If the blob still exists and its ETag is still valid, execute.
            // Note: it's possible the blob could change/be deleted between now and when the function executes.
            Guid? parentId = await _causalityReader.GetWriterAsync(blob, cancellationToken);
            TriggeredFunctionData input = new TriggeredFunctionData
            {
                ParentId = parentId,
                TriggerValue = blob
            };

            return await registration.Executor.TryExecuteAsync(input, cancellationToken);
        }
    }
}
