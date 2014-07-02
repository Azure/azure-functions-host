using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class FunctionIndexReader : IFunctionIndexReader
    {
        private readonly CloudBlobDirectory _directory;

        [CLSCompliant(false)]
        public FunctionIndexReader(CloudBlobClient blobClient)
            : this(blobClient.GetContainerReference(DashboardContainerNames.Dashboard)
                .GetDirectoryReference(DashboardDirectoryNames.Hosts))
        {
        }

        private FunctionIndexReader(CloudBlobDirectory directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException("directory");
            }

            _directory = directory;
        }

        public IResultSegment<FunctionSnapshot> Read(int maximumResults, string continuationToken)
        {
            int startIndex;

            if (continuationToken == null)
            {
                startIndex = 0;
            }
            else
            {
                int parsed;

                // Currently not doing server-side blob paging. Continuation token is just the start index.
                if (!Int32.TryParse(continuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
                {
                    // Invalid continuation token
                    return null;
                }

                if (parsed <= 0)
                {
                    // Invalid continuation token
                    return null;
                }

                startIndex = parsed;
            }

            List<FunctionSnapshot> functions = ReadAllFunctions();
            List<FunctionSnapshot> results = functions.Skip(startIndex).Take(maximumResults).ToList();
            int resultsCount = results.Count;

            string nextContinuationToken;

            if (resultsCount < maximumResults)
            {
                nextContinuationToken = null;
            }
            else
            {
                nextContinuationToken = (startIndex + resultsCount).ToString("g", CultureInfo.InvariantCulture);
            }

            return new ResultSegment<FunctionSnapshot>(results, nextContinuationToken);
        }

        private List<FunctionSnapshot> ReadAllFunctions()
        {
            List<HostSnapshot> hosts = ReadAllHosts();

            List<FunctionSnapshot> functions = new List<FunctionSnapshot>();

            foreach (HostSnapshot host in hosts)
            {
                foreach (FunctionSnapshot function in host.Functions)
                {
                    if (function == null)
                    {
                        continue;
                    }

                    // Add the HostVersion (not part of the JSON-serialized blob).
                    function.HostVersion = host.HostVersion;
                    functions.Add(function);
                }
            }

            return functions;
        }

        private List<HostSnapshot> ReadAllHosts()
        {
            IEnumerable<IListBlobItem> blobs = _directory.ListBlobs(useFlatBlobListing: true);

            List<HostSnapshot> hosts = new List<HostSnapshot>();

            // Cast from IListBlobItem to ICloudBlob is safe due to useFlatBlobListing: true above.
            foreach (ICloudBlob blob in blobs)
            {
                CloudBlockBlob blockBlob = blob as CloudBlockBlob;

                if (blockBlob == null)
                {
                    continue;
                }

                HostSnapshot host = FunctionLookup.ReadJson<HostSnapshot>(blockBlob);

                hosts.Add(host);
            }

            return hosts;
        }
    }
}
