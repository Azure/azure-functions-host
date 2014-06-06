using System;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class AbortRequestLogger : IAbortRequestLogger
    {
        private readonly CloudBlobContainer _container;

        [CLSCompliant(false)]
        public AbortRequestLogger(CloudBlobClient client)
            : this(client.GetContainerReference(DashboardContainerNames.AbortRequestLogContainer))
        {
        }

        private AbortRequestLogger(CloudBlobContainer container)
        {
            _container = container;
        }

        public void LogAbortRequest(string queueName)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(queueName);

            try
            {
                blob.UploadText(String.Empty);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    _container.CreateIfNotExists();
                    blob.UploadText(String.Empty);
                }
                else
                {
                    throw;
                }
            }
        }

        public bool HasRequestedAbort(string queueName)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(queueName);

            try
            {
                blob.FetchAttributes();
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
