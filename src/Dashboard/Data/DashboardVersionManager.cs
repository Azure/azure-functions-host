// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Dashboard.Data
{
    /// <summary>Represents a reader that provides dashboard version information.</summary>
    public class DashboardVersionManager : IDashboardVersionManager
    {
        public const DashboardVersionNumber CurrentDashboardVersion = DashboardVersionNumber.Version1;

        private readonly JsonConcurrentDocumentStore<DashboardVersion> _store;

        public IConcurrentDocument<DashboardVersion> CurrentVersion { get; set; }

        /// <summary>
        /// Instantiates a new instance of the <see cref="HostVersionReader"/> class.
        /// </summary>
        /// <param name="client">The cloud storage client.</param>
        [CLSCompliant(false)]
        public DashboardVersionManager(CloudBlobClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            IConcurrentMetadataTextStore innerStore = ConcurrentTextStore.CreateBlobStore(client, DashboardContainerNames.Dashboard, string.Empty);
            _store = new JsonConcurrentDocumentStore<DashboardVersion>(innerStore); 
        }

        /// <inheritdoc />
        [DebuggerNonUserCode]
        public IConcurrentDocument<DashboardVersion> Read()
        {
            var version = _store.Read(DashboardBlobNames.Version);
            if (version == null)
            {
                StartDeletingOldData(null);
                return _store.Read(DashboardBlobNames.Version);
            }

            return version;
        }

        public void StartDeletingOldData(string eTag)
        {
            var version = new DashboardVersion
            {
                Version = CurrentDashboardVersion,
                UpgradeState = DashboardUpgradeState.DeletingOldData
            };

            TryCreateOrUpdateVersion(version, eTag);
        }

        public void StartRestoringArchive(string eTag)
        {
            var version = new DashboardVersion
            {
                Version = CurrentDashboardVersion,
                UpgradeState = DashboardUpgradeState.RestoringArchive
            };

            TryCreateOrUpdateVersion(version, eTag);
        }

        public void FinishUpgrade(string eTag)
        {
            // Set the new version and the upgrade state
            var version = new DashboardVersion
            {
                Version = CurrentDashboardVersion,
                UpgradeState = DashboardUpgradeState.Finished
            };

            TryCreateOrUpdateVersion(version, eTag);
        }

        private void TryCreateOrUpdateVersion(DashboardVersion version, string eTag)
        {
            if (!String.IsNullOrEmpty(eTag))
            {
                _store.TryUpdate(DashboardBlobNames.Version, eTag, version);
            }
            else
            {
                _store.TryCreate(DashboardBlobNames.Version, version);
            }
        }
    }
}
