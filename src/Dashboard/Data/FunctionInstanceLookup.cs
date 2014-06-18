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

        private readonly CloudBlobContainer _container;

        public FunctionInstanceLookup(CloudBlobClient client)
            : this(client.GetContainerReference(DashboardContainerNames.FunctionLogContainerName))
        {
        }

        public FunctionInstanceLookup(CloudBlobContainer container)
        {
            if (container == null)
            {
                throw new ArgumentNullException("container");
            }

            _container = container;
        }

        FunctionInstanceSnapshot IFunctionInstanceLookup.Lookup(Guid id)
        {
            CloudBlockBlob blob = _container.GetBlockBlobReference(id.ToString("N"));
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
