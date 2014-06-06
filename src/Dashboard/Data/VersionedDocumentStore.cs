using System;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace Dashboard.Data
{
    // An IVersionedDocumentStore implented using JSON serialization and a blob container in Azure Storage.
    public class VersionedDocumentStore<TDocument> : IVersionedDocumentStore<TDocument>
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        private readonly CloudBlobContainer _container;

        [CLSCompliant(false)]
        public VersionedDocumentStore(CloudBlobContainer container)
        {
            _container = container;
        }

        public VersionedDocument<TDocument> Read(string id)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);
            string contents;

            try
            {
                contents = blob.DownloadText();
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

            TDocument document = JsonConvert.DeserializeObject<TDocument>(contents, _settings);
            string eTag = blob.Properties.ETag;
            return new VersionedDocument<TDocument>(document, eTag);
        }

        internal static JsonSerializerSettings JsonSerializerSettings
        {
            get { return _settings; }
        }

        public void CreateOrUpdate(string id, TDocument document)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);
            string contents = JsonConvert.SerializeObject(document, _settings);

            try
            {
                blob.UploadText(contents);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    _container.CreateIfNotExists();
                    blob.UploadText(contents);
                }
                else
                {
                    throw;
                }
            }
        }

        public bool TryCreate(string id, TDocument document)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);
            _container.CreateIfNotExists();
            string contents = JsonConvert.SerializeObject(document, _settings);

            return TryUploadTextAndCreateContainerIfNotExists(blob, contents, new AccessCondition { IfNoneMatchETag = "*" });
        }

        public bool TryUpdate(string id, TDocument document, string eTag)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);
            string contents = JsonConvert.SerializeObject(document, _settings);

            return TryUploadTextAndCreateContainerIfNotExists(blob, contents, new AccessCondition { IfMatchETag = eTag });
        }

        public bool TryDelete(string id, string eTag)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);

            try
            {
                blob.Delete(accessCondition: new AccessCondition { IfMatchETag = eTag });
            }
            catch (StorageException exception)
            {
                // The item may have already been deleted (409) or updated by someone else (412), or (unusual) the container
                // may have been deleted (404).
                if (exception.IsConflict() || exception.IsPreconditionFailed() || exception.IsNotFound())
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

        private bool TryUploadTextAndCreateContainerIfNotExists(CloudBlockBlob blob, string contents,
            AccessCondition accessCondition)
        {
            try
            {
                blob.UploadText(contents, accessCondition: accessCondition);
            }
            catch (StorageException exception)
            {
                if (exception.IsPreconditionFailed() || exception.IsConflict())
                {
                    return false;
                }
                else if (exception.IsNotFound())
                {
                    _container.CreateIfNotExists();

                    try
                    {
                        blob.UploadText(contents, accessCondition: accessCondition);
                    }
                    catch (StorageException retryException)
                    {
                        if (retryException.IsPreconditionFailed() || exception.IsConflict())
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
    }
}
