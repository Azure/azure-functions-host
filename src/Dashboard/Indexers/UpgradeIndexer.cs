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
using System.Threading;
using System.Web;

namespace Dashboard.Indexers
{
    internal class UpgradeIndexer : Indexer
    {
        private readonly IPersistentQueueReader<PersistentQueueMessage> _upgradeQueueReader;
        private readonly IDashboardVersionManager _dashboardVersionManager;
        private readonly CloudBlobClient _client;
        private readonly IConcurrentMetadataTextStore _functionsStore;
        private readonly IConcurrentMetadataTextStore _logsStore;

        public UpgradeIndexer(IPersistentQueueReader<PersistentQueueMessage> queueReader,
            IHostIndexer hostIndexer,
            IFunctionIndexer functionIndexer,
            IIndexerLogWriter logWriter,
            IDashboardVersionManager dashboardVersionReader,
            CloudBlobClient client)
            : base(queueReader, hostIndexer, functionIndexer, logWriter)
        {
            _dashboardVersionManager = dashboardVersionReader;
            _client = client;
            _functionsStore = ConcurrentTextStore.CreateBlobStore(_client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.FunctionsFlat);
            _logsStore = ConcurrentTextStore.CreateBlobStore(_client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.Logs);

            // From archive back to output
            _upgradeQueueReader = new PersistentQueueReader<PersistentQueueMessage>(client.GetContainerReference(ContainerNames.HostArchive),
                client.GetContainerReference(ContainerNames.HostOutput));
        }

        public override void Update()
        {
            if (_dashboardVersionManager.CurrentVersion == null)
            {
                _dashboardVersionManager.CurrentVersion = _dashboardVersionManager.Read();
            }

            if (_dashboardVersionManager.CurrentVersion.Document.UpgradeState != DashboardUpgradeState.Finished ||
                _dashboardVersionManager.CurrentVersion.Document.Version != DashboardVersionManager.CurrentDashboardVersion)
            {
                if (_dashboardVersionManager.CurrentVersion.Document.UpgradeState == DashboardUpgradeState.DeletingOldData ||
                    (_dashboardVersionManager.CurrentVersion.Document.Version != DashboardVersionManager.CurrentDashboardVersion &&
                     _dashboardVersionManager.CurrentVersion.Document.UpgradeState == DashboardUpgradeState.Finished))
                {
                    _dashboardVersionManager.CurrentVersion = StartDeletingOldData(_functionsStore, _dashboardVersionManager.CurrentVersion);
                    _dashboardVersionManager.CurrentVersion = StartDeletingOldData(_logsStore, _dashboardVersionManager.CurrentVersion);
                }

                if (_dashboardVersionManager.CurrentVersion.Document.UpgradeState == DashboardUpgradeState.DeletingOldData ||
                    _dashboardVersionManager.CurrentVersion.Document.UpgradeState == DashboardUpgradeState.RestoringArchive)
                {
                    _dashboardVersionManager.CurrentVersion = StartRestoringArchive(_dashboardVersionManager.CurrentVersion);
                }

                if (_dashboardVersionManager.CurrentVersion.Document.UpgradeState == DashboardUpgradeState.RestoringArchive)
                {
                    FinishUpdate(_dashboardVersionManager.CurrentVersion);
                }
            }

            base.Update();
        }

        private IConcurrentDocument<DashboardVersion> StartDeletingOldData(IConcurrentMetadataTextStore store, IConcurrentDocument<DashboardVersion> version)
        {
            // Set status to deletion status (using etag)
            _dashboardVersionManager.StartDeletingOldData(version.ETag);

            // Refresh version
            version = _dashboardVersionManager.Read();

            while (version.Document.UpgradeState == DashboardUpgradeState.DeletingOldData)
            {
                var items = store.List(null);

                // Refresh version
                version = _dashboardVersionManager.Read();

                // Return once everything's deleted
                if (items.Count() == 0 || version.Document.UpgradeState != DashboardUpgradeState.DeletingOldData)
                {
                    return version;
                }

                // Delete blobs
                foreach (var blob in items)
                {
                    DeleteIfLatest(store, blob);
                }
            }

            return version;
        }

        private void DeleteIfLatest(IConcurrentMetadataTextStore store, ConcurrentMetadata blob)
        {
            bool deleted = false;
            string previousETag = null;
            ConcurrentMetadata currentItem;

            for (currentItem = blob;
                !deleted && currentItem != null;
                currentItem = store.ReadMetadata(blob.Id))
            {
                string currentETag = currentItem.ETag;

                // Prevent an infinite loop if _innerStore erroneously returns false from TryDelete when a retry won't
                // help. (The inner store should throw rather than return false in that case.)
                if (currentETag == previousETag)
                {
                    throw new InvalidOperationException("The operation stopped making progress.");
                }

                previousETag = currentETag;
                deleted = _functionsStore.TryDelete(blob.Id, blob.ETag);
            }
        }

        private IConcurrentDocument<DashboardVersion> StartRestoringArchive(IConcurrentDocument<DashboardVersion> version)
        {
            const int IndexerPollIntervalMilliseconds = 5000;

            _dashboardVersionManager.StartRestoringArchive(version.ETag);

            PersistentQueueMessage message = _upgradeQueueReader.Dequeue();

            int count = 0;

            do
            {
                while (message != null)
                {
                    version = _dashboardVersionManager.Read();
                    if (version.Document.UpgradeState != DashboardUpgradeState.RestoringArchive)
                    {
                        _upgradeQueueReader.TryMakeItemVisible(message);
                        return version;
                    }

                    // Delete auto-"archives" from host-archive back to host-output.
                    _upgradeQueueReader.Delete(message);

                    message = _upgradeQueueReader.Dequeue();
                }

                version = _dashboardVersionManager.Read();
                if (version.Document.UpgradeState != DashboardUpgradeState.RestoringArchive)
                {
                    return version;
                }

                // Get items left
                // while limiting pagination to first page since we're only interested in
                // knowing if we're out of items.
                count = _upgradeQueueReader.Count(1);
                if (count > 0)
                {
                    // wait for a while before resuming
                    Thread.Sleep(IndexerPollIntervalMilliseconds);
                }
            }
            while (count > 0 );

            return version;
        }

        private void FinishUpdate(IConcurrentDocument<DashboardVersion> version)
        {
            _dashboardVersionManager.FinishUpgrade(version.ETag);
        }
    }
}
