// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    public enum DashboardUpgradeState
    {
        DeletingOldData,

        RestoringArchive,

        Finished
    }

    public enum Version
    {
        Version1
    }

    /// <summary>Represents a dashboard version.</summary>
    public class DashboardVersion
    {
        /// <summary>Gets or sets the etag.</summary>
        public string ETag { get; set; }

        /// <summary>Gets or sets a version.</summary>
        public Version Version { get; set; }

        /// <summary>Gets or sets the state of the upgrade.</summary>
        public DashboardUpgradeState Upgraded { get; set; }
    }
}
