// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class AbortRequestLogger : IAbortRequestLogger
    {
        private readonly CloudBlobDirectory _directory;

        [CLSCompliant(false)]
        public AbortRequestLogger(CloudBlobClient client)
            : this(client.GetContainerReference(DashboardContainerNames.Dashboard)
                .GetDirectoryReference(DashboardDirectoryNames.AbortRequestLogs))
        {
        }

        private AbortRequestLogger(CloudBlobDirectory directory)
        {
            _directory = directory;
        }

        public void LogAbortRequest(string queueName)
        {
            CloudBlockBlob blob = _directory.GetBlockBlobReference(queueName);

            try
            {
                blob.UploadText(String.Empty);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    blob.Container.CreateIfNotExists();
                    blob.UploadText(String.Empty);
                }
                else
                {
                    throw;
                }
            }
        }

        public bool HasRequestedAbort(string queueName)
        {
            CloudBlockBlob blob = _directory.GetBlockBlobReference(queueName);

            try
            {
                blob.FetchAttributes();
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
