using System;
using System.Diagnostics;
using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class HostIdManager : IHostIdManager
    {
        private readonly CloudBlobContainer _container;

        public HostIdManager(CloudBlobClient client)
            : this(VerifyNotNull(client).GetContainerReference(HostContainerNames.IdContainerName))
        {
        }

        public HostIdManager(CloudBlobContainer container)
        {
            if (container == null)
            {
                throw new ArgumentNullException("container");
            }

            _container = container;
        }

        public CloudBlobContainer Container
        {
            get { return _container; }
        }

        public Guid GetOrCreateHostId(string sharedHostName)
        {
            Debug.Assert(_container != null);

            CloudBlockBlob blob = _container.GetBlockBlobReference(sharedHostName);
            Guid hostId;

            if (TryGetExistingId(blob, out hostId))
            {
                return hostId;
            }

            Guid newHostId = Guid.NewGuid();

            if (TryInitializeId(blob, newHostId))
            {
                return newHostId;
            }

            if (TryGetExistingId(blob, out hostId))
            {
                return hostId;
            }

            // Not expected - valid host ID didn't exist before, couldn't be created, and still didn't exist after.
            throw new InvalidOperationException("Unable to determine host ID.");
        }

        private static bool TryGetExistingId(CloudBlockBlob blob, out Guid hostId)
        {
            string text;

            if (!TryDownload(blob, out text))
            {
                hostId = Guid.Empty;
                return false;
            }

            Guid possibleHostId;

            if (Guid.TryParseExact(text, "N", out possibleHostId))
            {
                hostId = possibleHostId;
                return true;
            }

            hostId = Guid.Empty;
            return false;
        }

        private static bool TryDownload(CloudBlockBlob blob, out string text)
        {
            try
            {
                text = blob.DownloadText();
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    text = null;
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }

        private static bool TryInitializeId(CloudBlockBlob blob, Guid hostId)
        {
            string text = hostId.ToString("N");
            AccessCondition accessCondition = new AccessCondition { IfNoneMatchETag = "*" };

            try
            {
                blob.UploadText(text, accessCondition: accessCondition);
            }
            catch (StorageException exception)
            {
                if (exception.IsPreconditionFailed() || exception.IsConflict())
                {
                    return false;
                }
                else if (exception.IsNotFound())
                {
                    blob.Container.CreateIfNotExists();

                    try
                    {
                        blob.UploadText(text, accessCondition: accessCondition);
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

        private static CloudBlobClient VerifyNotNull(CloudBlobClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            return client;
        }
    }
}
