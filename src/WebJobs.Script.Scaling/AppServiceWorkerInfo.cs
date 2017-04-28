// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    public class AppServiceWorkerInfo : TableEntity, IWorkerInfo
    {
        public AppServiceWorkerInfo()
        {
        }

        [IgnoreProperty]
        public string SiteName
        {
            get { return this.PartitionKey; }
        }

        public string StampName
        {
            get;
            set;
        }

        public string WorkerName
        {
            get;
            set;
        }

        public int LoadFactor
        {
            get;
            set;
        }

        [IgnoreProperty]
        public DateTime LastModifiedTimeUtc
        {
            get { return this.Timestamp.UtcDateTime; }
        }

        [IgnoreProperty]
        public bool IsStale
        {
            get
            {
                // no update for stale interval
                return this.LastModifiedTimeUtc.Add(ScaleSettings.Instance.StaleWorkerCheckInterval) < DateTime.UtcNow;
            }
        }

        [IgnoreProperty]
        public bool IsHomeStamp
        {
            get { return string.Equals(this.StampName, AppServiceSettings.HomeStampName, StringComparison.OrdinalIgnoreCase); }
        }
    }
}