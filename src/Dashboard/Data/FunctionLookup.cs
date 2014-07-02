using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Storage;
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

            FunctionSnapshot snapshot = hostSnapshot.Functions.FirstOrDefault(f => f.Id == functionId);

            if (snapshot == null)
            {
                return null;
            }

            // Add the HostVersion (not part of the JSON-serialized blob).
            snapshot.HostVersion = hostSnapshot.HostVersion;
            return snapshot;
        }

        internal static T ReadJson<T>(CloudBlockBlob blob)
        {
            string contents;

            try
            {
                contents = blob.DownloadText();
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFoundBlobOrContainerNotFound())
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
