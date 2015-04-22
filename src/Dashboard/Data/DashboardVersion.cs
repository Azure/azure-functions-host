// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    public enum DashboardUpgradeState
    {
        DeletingOldData = 0,

        RestoringArchive = 1,

        Finished = 2
    }

    public enum DashboardVersionNumber
    {
        Version1
    }

    /// <summary>Represents a dashboard version.</summary>
    public class DashboardVersion
    {
        /// <summary>Gets or sets a version.</summary>
        public DashboardVersionNumber Version { get; set; }

        /// <summary>Gets or sets the state of the upgrade.</summary>
        public DashboardUpgradeState UpgradeState { get; set; }
    }
}
