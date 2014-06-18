using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace Dashboard.Data
{
    internal class FunctionLookup : IFunctionLookup
    {
        private readonly CloudBlobContainer _container;

        public FunctionLookup(CloudBlobClient blobClient)
            : this(blobClient.GetContainerReference(DashboardContainerNames.HostContainerName))
        {
        }

        public FunctionLookup(CloudBlobContainer container)
        {
            if (container == null)
            {
                throw new ArgumentNullException("container");
            }

            _container = container;
        }

        public FunctionSnapshot Read(string functionId)
        {
            FunctionIdentifier functionIdentifier = FunctionIdentifier.Parse(functionId);
            HostSnapshot hostSnapshot = ReadJson<HostSnapshot>(_container, functionIdentifier.HostId.ToString());

            if (hostSnapshot == null)
            {
                return null;
            }

            return hostSnapshot.Functions.FirstOrDefault(f => f.Id == functionId);
        }

        public IReadOnlyList<FunctionSnapshot> ReadAll()
        {
            List<FunctionSnapshot> snapshots = new List<FunctionSnapshot>();

            try
            {
                foreach (ICloudBlob blob in _container.ListBlobs(useFlatBlobListing: true))
                {
                    HostSnapshot hostSnapshot = ReadJson<HostSnapshot>(_container, blob.Name);

                    if (hostSnapshot != null)
                    {
                        snapshots.AddRange(hostSnapshot.Functions);
                    }
                }
            }
            catch (StorageException exception)
            {
                RequestResult result = exception.RequestInformation;

                if (result != null && result.HttpStatusCode == 404)
                {
                    return snapshots;
                }
                else
                {
                    throw;
                }
            }

            return snapshots;
        }

        private static T ReadJson<T>(CloudBlobContainer container, string blobName)
        {
            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
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
                    return default(T);
                }
                else
                {
                    throw;
                }
            }

            return JsonConvert.DeserializeObject<T>(contents, JsonVersionedDocumentStore<T>.JsonSerializerSettings);
        }
    }
}
