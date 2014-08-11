// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
#if PUBLICPROTOCOL
using Microsoft.Azure.Jobs.Storage;
#else
using Microsoft.Azure.Jobs.Host.Storage;
#endif
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a persistent queue reader.</summary>
    /// <typeparam name="T">The type of messages in the queue.</typeparam>
#if PUBLICPROTOCOL
    [CLSCompliant(false)]
    public class PersistentQueueReader<T> : IPersistentQueueReader<T> where T : PersistentQueueMessage
#else
    internal class PersistentQueueReader<T> : IPersistentQueueReader<T> where T : PersistentQueueMessage
#endif
    {
        private const string NextVisibleTimeKey = "NextVisibleTime";
        private const string CreatedKey = "Created";

        private readonly CloudBlobContainer _outputContainer;
        private readonly CloudBlobContainer _archiveContainer;

        private ConcurrentQueue<ICloudBlob> _outputBlobs = new ConcurrentQueue<ICloudBlob>();
        private int _updating;

        /// <summary>Initializes a new instance of the <see cref="PersistentQueueReader{T}"/> class.</summary>
        /// <param name="client">
        /// A blob client for the storage account into which host output messages are written.
        /// </param>
        public PersistentQueueReader(CloudBlobClient client)
            : this(client.GetContainerReference(ContainerNames.HostOutput),
            client.GetContainerReference(ContainerNames.HostArchive))
        {
        }

        /// <summary>Initializes a new instance of the <see cref="PersistentQueueReader{T}"/> class.</summary>
        /// <param name="outputContainer">The container into which output messages are written by the host.</param>
        /// <param name="archiveContainer">
        /// The container into which host output messages are archived after processing.
        /// </param>
        public PersistentQueueReader(CloudBlobContainer outputContainer, CloudBlobContainer archiveContainer)
        {
            _outputContainer = outputContainer;
            _archiveContainer = archiveContainer;
        }

        /// <inheritdoc />
        public T Dequeue()
        {
            ICloudBlob possibleNextItem;
            T nextItem = null;
            DateTimeOffset createdOn;

            if (_outputBlobs.Count == 0 && Interlocked.CompareExchange(ref _updating, 1, 0) == 0)
            {
                try
                {
                    EnqueueNextVisibleItems(_outputBlobs);
                }
                finally
                {
                    Interlocked.Exchange(ref _updating, 0);
                }
            }

            // Keep racing to take ownership of the next visible item until that succeeds or there are no more left.
            while (_outputBlobs.TryDequeue(out possibleNextItem))
            {
                // There two reasons to keep racing:
                // 1. We tried to mark the item as invisible, and failed (409, someone else won the race)
                // 2. We then tried to download the item and failed (404, someone else finished processing an item, even
                // though we owned it).
                if (TryMakeItemInvisible(possibleNextItem, out createdOn) && TryDownloadItem(possibleNextItem,
                    createdOn, out nextItem))
                {
                    break;
                }
            }

            return nextItem;
        }

        /// <summary>
        /// Gets the number of messages in the queue
        /// </summary>
        /// <param name="limit">Only counts up to a certain limit. If zero, counts all</param>
        /// <returns>A positive value</returns>
        /// <remarks>Expensive operation when there are a lot of messages</remarks>
        public int Count(int? limit)
        {
            BlobResultSegment results = null;
            BlobContinuationToken continuationToken = null;

            int blobCount = 0;

            try
            {
                do
                {
                    results = _outputContainer.ListBlobsSegmented(
                        prefix: null,
                        useFlatBlobListing: true,
                        blobListingDetails: BlobListingDetails.None,
                        currentToken: continuationToken,
                        maxResults: limit,
                        options: null,
                        operationContext: null);

                    blobCount += results.Results.Count();
                    continuationToken = results.ContinuationToken;
                }
                while (continuationToken != null &&
                       (limit == null || blobCount < limit.Value));
            }
            catch (StorageException exception)
            {
                if (!exception.IsNotFound())
                {
                    throw;
                }
            }

            return blobCount;
        }

        private void EnqueueNextVisibleItems(ConcurrentQueue<ICloudBlob> results)
        {
            BlobContinuationToken currentToken = null;
            BlobResultSegment segment;

            do
            {
                segment = GetSegment(currentToken);

                if (segment == null)
                {
                    return;
                }

                currentToken = segment.ContinuationToken;

                if (segment.Results != null)
                {
                    // Cast from IListBlobItem to ICloudBlob is safe due to useFlatBlobListing: true in GetSegment.
                    foreach (ICloudBlob blob in segment.Results)
                    {
                        if (!blob.Metadata.ContainsKey(NextVisibleTimeKey) ||
                            IsInPast(blob.Metadata[NextVisibleTimeKey]))
                        {
                            results.Enqueue(blob);
                        }
                    }
                }
            } while (results.Count == 0 && currentToken != null);
        }

        private BlobResultSegment GetSegment(BlobContinuationToken currentToken)
        {
            const int batchSize = 100;

            try
            {
                return _outputContainer.ListBlobsSegmented(prefix: null,
                    useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.Metadata,
                    maxResults: batchSize,
                    currentToken: currentToken,
                    options: null,
                    operationContext: null);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }
        }

        private static bool IsInPast(string nextVisibleTimeValue)
        {
            DateTimeOffset nextVisibleTime;

            if (!DateTimeOffset.TryParseExact(nextVisibleTimeValue, "o", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out nextVisibleTime))
            {
                // Treat invalid next visible time values as already expired.
                return true;
            }

            return DateTimeOffset.UtcNow > nextVisibleTime;
        }

        private static bool TryMakeItemInvisible(ICloudBlob item, out DateTimeOffset createdOn)
        {
            // After this window expires, others may attempt to process the item.
            const double processingWindowInMinutes = 5;

            item.Metadata[NextVisibleTimeKey] =
                DateTimeOffset.UtcNow.AddMinutes(processingWindowInMinutes).ToString("o", CultureInfo.InvariantCulture);

            if (item.Metadata.ContainsKey(CreatedKey))
            {
                createdOn = GetCreatedOn(item);
            }
            else
            {
                createdOn = item.Properties.LastModified.GetValueOrDefault(DateTimeOffset.UtcNow);
                item.Metadata.Add(CreatedKey, createdOn.ToString("o", CultureInfo.InvariantCulture));
            }

            try
            {
                item.SetMetadata(new AccessCondition { IfMatchETag = item.Properties.ETag });
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.IsPreconditionFailed() || exception.IsNotFoundBlobNotFound())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }

        private static bool TryDownloadItem(ICloudBlob possibleNextItem, DateTimeOffset createdOn, out T nextItem)
        {
            string contents;

            try
            {
                using (Stream stream = possibleNextItem.OpenRead())
                {
                    using (TextReader reader = new StreamReader(stream))
                    {
                        contents = reader.ReadToEnd();
                    }
                }
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    // If the item no longer exists, someone else finished processing it, and we should look for another
                    // next item.
                    nextItem = null;
                    return false;
                }
                else
                {
                    throw;
                }
            }

            nextItem = JsonConvert.DeserializeObject<T>(contents, JsonSerialization.Settings);
            nextItem.Blob = possibleNextItem;
            nextItem.BlobText = contents;
            nextItem.EnqueuedOn = createdOn;
            nextItem.PopReceipt = possibleNextItem.Name;

            return true;
        }

        private static DateTimeOffset GetCreatedOn(ICloudBlob item)
        {
            string createdOnValue;

            if (item.Metadata.ContainsKey(CreatedKey))
            {
                createdOnValue = item.Metadata[CreatedKey];
            }
            else
            {
                // This should never happen, since we always add this metadata when taking ownership of the blob.
                createdOnValue = null;
            }

            DateTimeOffset createdOn;

            if (createdOnValue != null && DateTimeOffset.TryParseExact(createdOnValue, "o",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out createdOn))
            {
                return createdOn;
            }

            return item.Properties.LastModified.GetValueOrDefault(DateTimeOffset.UtcNow);
        }

        /// <inheritdoc />
        public void Delete(T message)
        {
            ICloudBlob outputBlob = message.Blob;

            // Do a client-side blob copy. Note that StartCopyFromBlob is another option, but that would require polling
            // to wait for completion.
            CloudBlockBlob archiveBlob = _archiveContainer.GetBlockBlobReference(message.PopReceipt);
            CopyProperties(outputBlob, archiveBlob);
            CopyMetadata(outputBlob, archiveBlob);
            archiveBlob.Metadata.RemoveIfContainsKey(NextVisibleTimeKey);

            try
            {
                archiveBlob.UploadText(message.BlobText);
            }
            catch (StorageException exception)
            {
                if (!exception.IsNotFound())
                {
                    throw;
                }
                else
                {
                    _archiveContainer.CreateIfNotExists();
                    archiveBlob.UploadText(message.BlobText);
                }
            }

            outputBlob.DeleteIfExists();
        }

        private static void CopyProperties(ICloudBlob source, ICloudBlob destination)
        {
            BlobProperties sourceProperties = source.Properties;
            BlobProperties destinationProperties = destination.Properties;

            destinationProperties.CacheControl = sourceProperties.CacheControl;
            destinationProperties.ContentDisposition = sourceProperties.ContentDisposition;
            destinationProperties.ContentEncoding = sourceProperties.ContentEncoding;
            destinationProperties.ContentLanguage = sourceProperties.ContentLanguage;
            destinationProperties.ContentMD5 = sourceProperties.ContentMD5;
            destinationProperties.ContentType = sourceProperties.ContentType;
        }

        private static void CopyMetadata(ICloudBlob source, ICloudBlob destination)
        {
            IDictionary<string, string> sourceMetadata = source.Metadata;
            IDictionary<string, string> destinationMetadata = destination.Metadata;

            foreach (KeyValuePair<string, string> metadata in sourceMetadata)
            {
                destinationMetadata.Add(metadata.Key, metadata.Value);
            }
        }
    }
}
