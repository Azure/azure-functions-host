using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    // An IConcurrentTextStore & IConcurrentMetadataTextStore implemented using a blob directory in Azure Storage.
    public class BlobConcurrentTextStore : IConcurrentTextStore, IConcurrentMetadataTextStore
    {
        private readonly CloudBlobDirectory _directory;

        [CLSCompliant(false)]
        public BlobConcurrentTextStore(CloudBlobDirectory directory)
        {
            _directory = directory;
        }

        public IEnumerable<ConcurrentMetadata> List(string prefix)
        {
            string combinedPrefix;

            if (String.IsNullOrEmpty(prefix))
            {
                combinedPrefix = _directory.Prefix;
            }
            else
            {
                combinedPrefix = _directory.Prefix + prefix;
            }

            IEnumerable<IListBlobItem> items = _directory.Container.ListBlobs(combinedPrefix, useFlatBlobListing: true,
                blobListingDetails: BlobListingDetails.Metadata);

            List<ConcurrentMetadata> returnValue = new List<ConcurrentMetadata>();

            // Type cast to ICloudBlob is safe due to useFlatBlobListing: true above.
            foreach (ICloudBlob blob in items)
            {
                // Remove the combined prefix and slash before the ID.
                string id = blob.Name.Substring(combinedPrefix.Length + 1);
                returnValue.Add(new ConcurrentMetadata(id, blob.Properties.ETag, blob.Metadata));
            }

            return returnValue;
        }

        IConcurrentText IConcurrentTextStore.Read(string id)
        {
            return ((IConcurrentMetadataTextStore)this).Read(id);
        }

        public ConcurrentMetadataText Read(string id)
        {
            CloudBlockBlob blob = _directory.GetBlockBlobReference(id);
            string text;

            try
            {
                text = blob.DownloadText();
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFoundBlobOrContainerNotFound())
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }

            return new ConcurrentMetadataText(blob.Properties.ETag, blob.Metadata, text);
        }

        public ConcurrentMetadata ReadMetadata(string id)
        {
            CloudBlockBlob blob = _directory.GetBlockBlobReference(id);

            try
            {
                blob.FetchAttributes();
            }
            catch (StorageException exception)
            {
                // Note: Can't use IsNotFoundBlobOrContainerNotFound because the error code comes from the body and
                // FetchAttributes is a HEAD request, so there's no error body in this case.
                if (exception.IsNotFound())
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }

            return new ConcurrentMetadata(id, blob.Properties.ETag, blob.Metadata);
        }

        public void CreateOrUpdate(string id, string text)
        {
            CloudBlockBlob blob = _directory.GetBlockBlobReference(id);

            try
            {
                blob.UploadText(text);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFoundContainerNotFound())
                {
                    blob.Container.CreateIfNotExists();
                    blob.UploadText(text);
                }
                else
                {
                    throw;
                }
            }
        }

        public void DeleteIfExists(string id)
        {
            CloudBlockBlob blob = _directory.GetBlockBlobReference(id);

            blob.DeleteIfExists();
        }

        public bool TryCreate(string id, string text)
        {
            return TryCreate(id, metadata: null, text: text);
        }

        public bool TryCreate(string id, IDictionary<string, string> metadata, string text)
        {
            CloudBlockBlob blob = _directory.GetBlockBlobReference(id);
            CopyMetadata(metadata, blob);

            AccessCondition accessCondition = new AccessCondition { IfNoneMatchETag = "*" };

            try
            {
                blob.UploadText(text, accessCondition: accessCondition);
            }
            catch (StorageException exception)
            {
                if (exception.IsConflictBlobAlreadyExists())
                {
                    return false;
                }
                else if (exception.IsNotFoundContainerNotFound())
                {
                    blob.Container.CreateIfNotExists();

                    try
                    {
                        blob.UploadText(text, accessCondition: accessCondition);
                    }
                    catch (StorageException retryException)
                    {
                        if (retryException.IsConflictBlobAlreadyExists())
                        {
                            return false;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
                else
                {
                    throw;
                }
            }

            return true;
        }

        public bool TryUpdate(string id, string eTag, string text)
        {
            return TryUpdate(id, eTag, metadata: null, text: text);
        }

        public bool TryUpdate(string id, string eTag, IDictionary<string, string> metadata, string text)
        {
            CloudBlockBlob blob = _directory.GetBlockBlobReference(id);
            CopyMetadata(metadata, blob);

            AccessCondition accessCondition = new AccessCondition { IfMatchETag = eTag };

            try
            {
                blob.UploadText(text, accessCondition: accessCondition);
            }
            catch (StorageException exception)
            {
                // Note: unlike TryCreate, there's no value creating the container if it doesn't exist (the update can't
                // succeed because the blob itself doesn't exist so of course the ETag can't match).
                if (exception.IsPreconditionFailedConditionNotMet() || exception.IsNotFoundBlobOrContainerNotFound())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }

            return true;
        }

        public bool TryDelete(string id, string eTag)
        {
            CloudBlockBlob blob = _directory.GetBlockBlobReference(id);

            try
            {
                blob.Delete(accessCondition: new AccessCondition { IfMatchETag = eTag });
            }
            catch (StorageException exception)
            {
                // The item may have already been deleted (404) or updated by someone else (412) or the container may
                // have been deleted (also 404).
                if (exception.IsNotFoundBlobOrContainerNotFound() || exception.IsPreconditionFailedConditionNotMet())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }

            return true;
        }

        private static void CopyMetadata(IDictionary<string, string> metadata, ICloudBlob blob)
        {
            if (metadata != null)
            {
                // Copy metadata from the argument to the blob.
                // No nead to clear first; the blob reference was just created by the caller.

                foreach (KeyValuePair<string, string> item in metadata)
                {
                    blob.Metadata.Add(item);
                }
            }
        }
    }
}
