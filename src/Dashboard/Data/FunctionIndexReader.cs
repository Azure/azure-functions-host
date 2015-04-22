// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class FunctionIndexReader : IFunctionIndexReader
    {
        private readonly CloudBlobContainer _functionsContainer;
        private readonly string _functionsPrefix;
        private readonly CloudBlobDirectory _versionDirectory;
        private readonly IVersionMetadataMapper _versionMapper;

        [CLSCompliant(false)]
        public FunctionIndexReader(CloudBlobClient client)
            : this (client.GetContainerReference(DashboardContainerNames.Dashboard).GetDirectoryReference(
                DashboardDirectoryNames.FunctionsFlat), client.GetContainerReference(DashboardContainerNames.Dashboard)
                .GetDirectoryReference(DashboardDirectoryNames.Functions), VersionMetadataMapper.Instance)
        {
        }

        private FunctionIndexReader(CloudBlobDirectory functionsDirectory, CloudBlobDirectory versionDirectory,
            IVersionMetadataMapper versionMapper)
        {
            if (functionsDirectory == null)
            {
                throw new ArgumentNullException("functionsDirectory");
            }
            else if (versionDirectory == null)
            {
                throw new ArgumentNullException("versionDirectory");
            }
            else if (versionMapper == null)
            {
                throw new ArgumentNullException("versionMapper");
            }

            _functionsContainer = functionsDirectory.Container;
            _functionsPrefix = functionsDirectory.Prefix;
            _versionDirectory = versionDirectory;
            _versionMapper = versionMapper;
        }

        public DateTimeOffset GetCurrentVersion()
        {
            CloudBlockBlob blob = _versionDirectory.GetBlockBlobReference(FunctionIndexVersionManager.VersionBlobName);

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

        public IResultSegment<FunctionIndexEntry> Read(int maximumResults, string continuationToken)
        {
            BlobContinuationToken blobContinuationToken = BlobContinuationTokenSerializer.Deserialize(continuationToken);

            BlobResultSegment blobSegment;

            try
            {
                blobSegment = _functionsContainer.ListBlobsSegmented(
                    prefix: _functionsPrefix,
                    useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.Metadata,
                    maxResults: maximumResults,
                    currentToken: blobContinuationToken,
                    options: null,
                    operationContext: null);
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

            if (blobSegment == null)
            {
                return null;
            }

            List<FunctionIndexEntry> results = new List<FunctionIndexEntry>();

            // Cast from IListBlobItem to ICloudBlob is safe due to useFlatBlobListing: true above.
            foreach (ICloudBlob blob in blobSegment.Results)
            {
                IDictionary<string, string> metadata = blob.Metadata;
                DateTimeOffset version = _versionMapper.GetVersion(metadata);
                FunctionIndexEntry result = FunctionIndexEntry.Create(metadata, version);
                results.Add(result);
            }

            string nextContinuationToken = BlobContinuationTokenSerializer.Serialize(blobSegment.ContinuationToken);

            return new ResultSegment<FunctionIndexEntry>(results, nextContinuationToken);
        }
    }
}
