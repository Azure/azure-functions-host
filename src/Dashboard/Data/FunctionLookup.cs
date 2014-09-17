// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Storage;
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
            CloudBlockBlob blob = _directory.GetBlockBlobReference(functionIdentifier.HostId);
            HostSnapshot hostSnapshot = ReadJson<HostSnapshot>(blob);

            if (hostSnapshot == null)
            {
                return null;
            }

            return hostSnapshot.Functions.FirstOrDefault(f => f.Id == functionId);
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
