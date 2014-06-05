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
                RequestResult result = exception.RequestInformation;

                if (result != null && result.HttpStatusCode == 404)
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

        public bool TryCreate(string id, TDocument document)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);
            _container.CreateIfNotExists();
            string contents = JsonConvert.SerializeObject(document, _settings);

            try
            {
                blob.UploadText(contents, accessCondition: new AccessCondition { IfNoneMatchETag = "*" });
            }
            catch (StorageException exception)
            {
                if (exception.IsPreconditionFailed())
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

        public bool TryUpdate(string id, VersionedDocument<TDocument> versionedDocument)
        {
            if (versionedDocument == null)
            {
                throw new ArgumentNullException("versionedDocument");
            }

            TDocument document = versionedDocument.Document;

            CloudBlockBlob blob = _container.GetBlockBlobReference(id);
            string contents = JsonConvert.SerializeObject(document, _settings);

            try
            {
                blob.UploadText(contents, accessCondition: new AccessCondition { IfMatchETag = versionedDocument.ETag });
            }
            catch (StorageException exception)
            {
                RequestResult result = exception.RequestInformation;

                if (exception.IsPreconditionFailed())
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
            CloudBlockBlob blob = _container.GetBlockBlobReference(id);

            try
            {
                blob.Delete(accessCondition: new AccessCondition { IfMatchETag = eTag });
            }
            catch (StorageException exception)
            {
                // The item may have already been deleted (409) or updated by someone else (412).
                if (exception.IsConflict() || exception.IsPreconditionFailed())
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
    }
}
