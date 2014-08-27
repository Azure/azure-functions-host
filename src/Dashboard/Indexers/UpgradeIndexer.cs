// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Dashboard.Data;
using Dashboard.Data.Logs;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Dashboard.Indexers
{
    internal class UpgradeIndexer : Indexer
    {
        private readonly IPersistentQueueReader<PersistentQueueMessage> _upgradeQueueReader;
        private readonly IDashboardVersionManager _dasboardVersionReader;
        private readonly CloudBlobClient _client;
        private readonly IConcurrentMetadataTextStore _store;
        private const Data.Version CurrentVersion = Data.Version.Version1;

        public UpgradeIndexer(IPersistentQueueReader<PersistentQueueMessage> queueReader,
            IHostIndexer hostIndexer,
            IFunctionIndexer functionIndexer,
            IIndexerLogWriter logWriter,
            IDashboardVersionManager dashboardVersionReader,
            CloudBlobClient client)
            : base(queueReader, hostIndexer, functionIndexer, logWriter)
        {
            _dasboardVersionReader = dashboardVersionReader;
            _client = client;
            _store = ConcurrentTextStore.CreateBlobStore(_client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.Functions);


            // From archive back to output
            _upgradeQueueReader = new PersistentQueueReader<PersistentQueueMessage>(client.GetContainerReference(ContainerNames.HostArchive),
                client.GetContainerReference(ContainerNames.HostOutput));
        }

        public override void Update()
        {
            DashboardVersion version = _dasboardVersionReader.Read();

            if (version.Upgraded != DashboardUpgradeState.Finished)
            {
                if (version.Upgraded == DashboardUpgradeState.DeletingOldData ||
                    (version.Version != CurrentVersion &&
                     version.Upgraded == DashboardUpgradeState.Finished))
                {
                    version = StartDeletingOldData(version);
                }

                if (version.Upgraded == DashboardUpgradeState.DeletingOldData ||
                    version.Upgraded == DashboardUpgradeState.RestoringArchive)
                {
                    version = StartRestoringArchive(version);
                }

                if (version.Upgraded == DashboardUpgradeState.RestoringArchive)
                {
                    FinishUpdate(version);
                }
            }

            base.Update();
        }

        private DashboardVersion StartDeletingOldData(DashboardVersion version)
        {
            // Set status to deletion status (using etag)
            version = _dasboardVersionReader.StartDeletingOldData(version);

            while (version.Upgraded == DashboardUpgradeState.DeletingOldData)
            {
                var items = _store.List(null);

                // Refresh version
                version = _dasboardVersionReader.Read();

                // Return once everything's deleted
                if (items.Count() == 0 || version.Upgraded != DashboardUpgradeState.DeletingOldData)
                {
                    return version;
                }

                // Delete blobs
                foreach (var blob in items)
                {
                    _store.TryDelete(blob.Id, blob.ETag);
                }
            }

            return version;
        }

        private DashboardVersion StartRestoringArchive(DashboardVersion version)
        {
            version = _dasboardVersionReader.StartRestoringArchive(version);

            PersistentQueueMessage message = _upgradeQueueReader.Dequeue();

            while (message != null)
            {
                version = _dasboardVersionReader.Read();
                if (version.Upgraded != DashboardUpgradeState.RestoringArchive)
                {
                    _upgradeQueueReader.Enqueue(message);
                    return version;
                }

                _upgradeQueueReader.Delete(message);

                message = _upgradeQueueReader.Dequeue();
            }

            return version;
        }

        private void FinishUpdate(DashboardVersion version)
        {
            _dasboardVersionReader.FinishUpgrade(version);
        }
    }
}