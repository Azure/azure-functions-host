// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace Dashboard.Data.Logs
{
    internal class IndexerBlobLogWriter : IIndexerLogWriter
    {
        private readonly CloudBlobContainer _logsContainer;

        private readonly string _containerDirectory;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobLogWriter{T}"/> class.
        /// </summary>
        /// <param name="client">The blob client.</param>
        public IndexerBlobLogWriter(CloudBlobClient client)
            : this(client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.IndexerLog)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobLogWriter{T}"/> class.
        /// </summary>
        /// <param name="client">The blob client.</param>
        /// <param name="logsContainer">The logs container.</param>
        /// <param name="containerDirectory">The container directory.</param>
        public IndexerBlobLogWriter(CloudBlobClient client, string logsContainer, string containerDirectory)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            if (string.IsNullOrEmpty(logsContainer))
            {
                throw new ArgumentNullException("logsContainer");
            }

            if (containerDirectory == null)
            {
                containerDirectory = string.Empty;
            }

            _logsContainer = client.GetContainerReference(logsContainer);
            _containerDirectory = containerDirectory;
        }

        public void Write(IndexerLogEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            string logBlobName = BlobNames.GetConflictFreeDateTimeBasedBlobName(entry.Date);
            entry.Id = logBlobName;

            string logEntryAsJson = JsonConvert.SerializeObject(entry, JsonSerialization.Settings);

            _logsContainer.CreateIfNotExists();

            CloudBlockBlob logBlob = _logsContainer.GetBlockBlobReference(_containerDirectory + "/" + logBlobName);

            logBlob.Metadata[BlobLogEntryKeys.TitleKey] = entry.Title;
            logBlob.Metadata[BlobLogEntryKeys.LogDate] = entry.Date.ToString(CultureInfo.InvariantCulture);

            logBlob.UploadText(logEntryAsJson);
        }
    }
}
