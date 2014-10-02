// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class BlobTriggerExecutor : ITriggerExecutor<IStorageBlob>
    {
        private readonly string _hostId;
        private readonly string _functionId;
        private readonly IBlobPathSource _input;
        private readonly IBlobTriggerQueueWriter _queueWriter;
        private readonly IBlobETagReader _eTagReader;
        private readonly IBlobReceiptManager _receiptManager;

        public BlobTriggerExecutor(string hostId, string functionId, IBlobPathSource input,
            IBlobETagReader eTagReader, IBlobReceiptManager receiptManager, IBlobTriggerQueueWriter queueWriter)
        {
            _hostId = hostId;
            _functionId = functionId;
            _input = input;
            _queueWriter = queueWriter;
            _eTagReader = eTagReader;
            _receiptManager = receiptManager;
        }

        public async Task<bool> ExecuteAsync(IStorageBlob value, CancellationToken cancellationToken)
        {
            // Avoid unnecessary network calls for non-matches. First, check to see if the blob matches this trigger.
            IReadOnlyDictionary<string, object> bindingData = _input.CreateBindingData(value.ToBlobPath());

            if (bindingData == null)
            {
                // Blob is not a match for this trigger.
                return true;
            }

            // Next, check to see if the blob currently exists (and, if so, what the current ETag is).
            string possibleETag = await _eTagReader.GetETagAsync(value.SdkObject, cancellationToken);

            if (possibleETag == null)
            {
                // If the blob doesn't exist and have an ETag, don't trigger on it.
                return true;
            }

            CloudBlockBlob receiptBlob = _receiptManager.CreateReference(_hostId, _functionId, value.Container.Name,
                value.Name, possibleETag);

            // Check for the completed receipt. If it's already there, noop.
            BlobReceipt unleasedReceipt = await _receiptManager.TryReadAsync(receiptBlob, cancellationToken);

            if (unleasedReceipt != null && unleasedReceipt.IsCompleted)
            {
                return true;
            }
            else if (unleasedReceipt == null)
            {
                // Try to create (if not exists) an incomplete receipt.
                if (!await _receiptManager.TryCreateAsync(receiptBlob, cancellationToken))
                {
                    // Someone else just created the receipt; wait to try to trigger until later.
                    // Alternatively, we could just ignore the return result and see who wins the race to acquire the
                    // lease.
                    return false;
                }
            }

            string leaseId = await _receiptManager.TryAcquireLeaseAsync(receiptBlob, cancellationToken);

            if (leaseId == null)
            {
                // If someone else owns the lease and just took over this receipt or deleted it;
                // wait to try to trigger until later.
                return false;
            }

            ExceptionDispatchInfo exceptionInfo;

            try
            {
                // Check again for the completed receipt. If it's already there, noop.
                BlobReceipt receipt = await _receiptManager.TryReadAsync(receiptBlob, cancellationToken);
                Debug.Assert(receipt != null); // We have a (30 second) lease on the blob; it should never disappear on us.

                if (receipt.IsCompleted)
                {
                    await _receiptManager.ReleaseLeaseAsync(receiptBlob, leaseId, cancellationToken);
                    return true;
                }

                // We've successfully acquired a lease to enqueue the message for this blob trigger. Enqueue the message,
                // complete the receipt and release the lease.

                // Enqueue a message: function ID + blob path + ETag
                BlobTriggerMessage message = new BlobTriggerMessage
                {
                    FunctionId = _functionId,
                    BlobType = value.BlobType,
                    ContainerName = value.Container.Name,
                    BlobName = value.Name,
                    ETag = possibleETag
                };
                await _queueWriter.EnqueueAsync(message, cancellationToken);

                // Complete the receipt & release the lease
                await _receiptManager.MarkCompletedAsync(receiptBlob, leaseId, cancellationToken);
                await _receiptManager.ReleaseLeaseAsync(receiptBlob, leaseId, cancellationToken);

                return true;
            }
            catch (Exception exception)
            {
                exceptionInfo = ExceptionDispatchInfo.Capture(exception);
            }

            Debug.Assert(exceptionInfo != null);
            await _receiptManager.ReleaseLeaseAsync(receiptBlob, leaseId, cancellationToken);
            exceptionInfo.Throw();
            return false; // Keep the compiler happy; we'll never get here.
        }
    }
}
