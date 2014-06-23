using System;
using Microsoft.Azure.Jobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace Dashboard.Data
{
    internal class FunctionInstanceLookup : IFunctionInstanceLookup
    {
        private static readonly JsonSerializerSettings _settings =
            JsonVersionedDocumentStore<FunctionInstanceSnapshot>.JsonSerializerSettings;

        private readonly CloudBlobDirectory _directory;

        public FunctionInstanceLookup(CloudBlobClient client)
            : this(client.GetContainerReference(DashboardContainerNames.Dashboard)
            .GetDirectoryReference(DashboardDirectoryNames.FunctionInstances))
        {
        }

        public FunctionInstanceLookup(CloudBlobDirectory directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException("directory");
            }

            _directory = directory;
        }

        FunctionInstanceSnapshot IFunctionInstanceLookup.Lookup(Guid id)
        {
            CloudBlockBlob blob = _directory.GetBlockBlobReference(id.ToString("N"));
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

            return JsonConvert.DeserializeObject<FunctionInstanceSnapshot>(contents, _settings);
        }
   }
}
