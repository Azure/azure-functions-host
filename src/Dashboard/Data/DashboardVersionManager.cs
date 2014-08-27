// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        private readonly CloudBlobContainer _dashboardContainer;

        /// <summary>
        /// Instantiates a new instance of the <see cref="HostVersionReader"/> class.
        /// </summary>
        /// <param name="account">The cloud storage account.</param>
        [CLSCompliant(false)]
        public DashboardVersionManager(CloudBlobClient client)
            : this(client.GetContainerReference(DashboardContainerNames.Dashboard))
        {
        }

        private DashboardVersionManager(CloudBlobContainer dashboardContainer)
        {
            if (dashboardContainer == null)
            {
                throw new ArgumentNullException("dashboardContainer");
            }

            _dashboardContainer = dashboardContainer;
        }

        /// <inheritdoc />
        [DebuggerNonUserCode]
        public DashboardVersion Read()
        {
            var versionBlob = _dashboardContainer.GetBlockBlobReference(DashboardDirectoryNames.Version);
            if (versionBlob.Exists())
            {
                return GetDashboardVersion(versionBlob);
            }

            return new DashboardVersion() { Upgraded = DashboardUpgradeState.DeletingOldData };
        }

        public DashboardVersion StartDeletingOldData(DashboardVersion previousVersion)
        {
            try
            {
                var version = new DashboardVersion();
                version.ETag = previousVersion.ETag;
                version.Upgraded = DashboardUpgradeState.DeletingOldData;
                TryUpdateVersion(version);

                return version;
            }
            catch
            {
                return null;
            }
        }

        public DashboardVersion StartRestoringArchive(DashboardVersion previousVersion)
        {
            try
            {
                var version = new DashboardVersion();
                version.ETag = previousVersion.ETag;
                version.Upgraded = DashboardUpgradeState.RestoringArchive;
                TryUpdateVersion(version);

                return version;
            }
            catch
            {
                return null;
            }
        }

        public DashboardVersion FinishUpgrade(DashboardVersion previousVersion)
        {
            try
            {
                var version = new DashboardVersion();
                // Set the new version and the upgrade state
                version.Version = Data.Version.Version1;
                version.ETag = previousVersion.ETag;
                version.Upgraded = DashboardUpgradeState.Finished;
                TryUpdateVersion(version);

                return version;
            }
            catch
            {
                return null;
            }
        }

        private void TryUpdateVersion(DashboardVersion version)
        {
            try
            {
                _dashboardContainer.CreateIfNotExists();
            }
            catch (StorageException e)
            {
                // Conflicts should be ignored
                if (e.RequestInformation.HttpStatusCode != 409)
                {
                    throw e;
                }
            }

            try
            {
                var versionBlob = _dashboardContainer.GetBlockBlobReference(DashboardDirectoryNames.Version);
                string messageBody = JsonConvert.SerializeObject(version, _settings);
                versionBlob.Properties.ContentType = "application/json";
                versionBlob.UploadText(messageBody, accessCondition: new AccessCondition { IfMatchETag = version.ETag });
            }
            catch (StorageException e)
            {
                // Conflicts should be ignored
                if (e.RequestInformation.HttpStatusCode != 409)
                {
                    throw;
                }
            }
        }

        private static DashboardVersion GetDashboardVersion(ICloudBlob blob)
        {
            DashboardVersion version = null;

            if (blob.Properties.ContentType == "application/json")
            {
                Encoding utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                string value;

                using (Stream stream = blob.OpenRead())
                using (TextReader textReader = new StreamReader(stream, utf8))
                {
                    value = textReader.ReadToEnd();
                }

                version = JsonConvert.DeserializeObject<DashboardVersion>(value);
                version.ETag = blob.Properties.ETag;
            }
            else
            {
                throw new NotSupportedException();
            }

            return version;
        }
    }
}
