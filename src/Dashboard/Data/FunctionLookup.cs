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
        private readonly CloudBlobDirectory _directory;

        public FunctionLookup(CloudBlobClient blobClient)
            : this(blobClient.GetContainerReference(DashboardContainerNames.Dashboard)
                .GetDirectoryReference(DashboardDirectoryNames.Hosts))
        {
        }

        public FunctionLookup(CloudBlobDirectory directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException("directory");
            }

            _directory = directory;
        }

        public FunctionSnapshot Read(string functionId)
        {
            FunctionIdentifier functionIdentifier = FunctionIdentifier.Parse(functionId);
            CloudBlockBlob blob = _directory.GetBlockBlobReference(functionIdentifier.HostId.ToString());
            HostSnapshot hostSnapshot = ReadJson<HostSnapshot>(blob);

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
                foreach (ICloudBlob blob in _directory.ListBlobs(useFlatBlobListing: true))
                {
                    CloudBlockBlob blockBlob = blob as CloudBlockBlob;

                    if (blockBlob == null)
                    {
                        continue;
                    }

                    HostSnapshot hostSnapshot = ReadJson<HostSnapshot>(blockBlob);

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

        private static T ReadJson<T>(CloudBlockBlob blob)
        {
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
