using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Caching;
using Microsoft.Azure.Jobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class FunctionIndexReader : IFunctionIndexReader
    {
        internal static string CacheInvalidationKey = "FunctionsValid";

        private readonly CloudBlobDirectory _hostsDirectory;
        private readonly Cache _cache;
        private readonly CloudBlobDirectory _functionsDirectory;
        private readonly IVersionMetadataMapper _versionMapper;

        [CLSCompliant(false)]
        public FunctionIndexReader(CloudBlobClient blobClient, Cache cache)
            : this(blobClient.GetContainerReference(DashboardContainerNames.Dashboard).GetDirectoryReference(
                DashboardDirectoryNames.Hosts), cache, blobClient.GetContainerReference(
                DashboardContainerNames.Dashboard).GetDirectoryReference(DashboardDirectoryNames.Functions),
                VersionMetadataMapper.Instance)
        {
        }

        private FunctionIndexReader(CloudBlobDirectory hostsDirectory, Cache cache,
            CloudBlobDirectory functionsDirectory, IVersionMetadataMapper versionMapper)
        {
            if (hostsDirectory == null)
            {
                throw new ArgumentNullException("hostsDirectory");
            }
            else if (functionsDirectory == null)
            {
                throw new ArgumentNullException("functionsDirectory");
            }
            else if (versionMapper == null)
            {
                throw new ArgumentNullException("versionMapper");
            }

            _hostsDirectory = hostsDirectory;
            _cache = cache;
            _functionsDirectory = functionsDirectory;
            _versionMapper = versionMapper;
        }

        public DateTimeOffset GetCurrentVersion()
        {
            CloudBlockBlob blob = _functionsDirectory.GetBlockBlobReference(
                FunctionIndexVersionManager.VersionBlobName);

            try
            {
                blob.FetchAttributes();
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return DateTimeOffset.MinValue;
                }
                else
                {
                    throw;
                }
            }

            return _versionMapper.GetVersion(blob.Metadata);
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

            if (functions == null)
            {
                return null;
            }

            List<FunctionSnapshot> results = functions.Skip(startIndex).Take(maximumResults).ToList();
            int resultsCount = results.Count;

            string nextContinuationToken;

            if (resultsCount < maximumResults)
            {
                nextContinuationToken = null;
            }
            else
            {
                nextContinuationToken = (startIndex + resultsCount).ToString(CultureInfo.InvariantCulture);
            }

            return new ResultSegment<FunctionSnapshot>(results, nextContinuationToken);
        }

        private List<FunctionSnapshot> ReadAllFunctions()
        {
            const string CacheKey = "Functions";

            if (_cache != null)
            {
                List<FunctionSnapshot> cached = _cache.Get(CacheKey) as List<FunctionSnapshot>;

                if (cached != null)
                {
                    return cached;
                }
            }

            List<FunctionSnapshot> results = ReadAllFunctionsUncached();

            if (results == null)
            {
                return null;
            }

            if (_cache != null)
            {
                _cache.Insert(CacheInvalidationKey, true);
                _cache.Insert(CacheKey, results, new CacheDependency(null, new string[] { CacheInvalidationKey }));
            }

            return results;
        }

        private List<FunctionSnapshot> ReadAllFunctionsUncached()
        {
            List<HostSnapshot> hosts = ReadAllHosts();

            List<FunctionSnapshot> functions = new List<FunctionSnapshot>();

            if (hosts == null)
            {
                return null;
            }

            foreach (HostSnapshot host in hosts)
            {
                if (host == null || host.Functions == null)
                {
                    continue;
                }

                foreach (FunctionSnapshot function in host.Functions)
                {
                    functions.Add(function);
                }
            }

            return functions;
        }

        private List<HostSnapshot> ReadAllHosts()
        {
            IEnumerable<IListBlobItem> blobs;

            try
            {
                blobs = _hostsDirectory.ListBlobs(useFlatBlobListing: true).ToList();
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
