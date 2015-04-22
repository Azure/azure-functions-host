// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    /// <summary>Defines a manager that manages dashboard version information.</summary>
    public interface IDashboardVersionManager
    {
        IConcurrentDocument<DashboardVersion> CurrentVersion { get; set; }

        /// <summary>Reads the data dashboard version.</summary>
        /// <returns>The dashboard version.</returns>
        IConcurrentDocument<DashboardVersion> Read();

        /// <summary>Start the process of deleting old statistics.</summary>
        void StartDeletingOldData(string etag);

        /// <summary>Start the process of restoring the archive.</summary>
        void StartRestoringArchive(string etag);

        /// <summary>Finishes the upgrade process.</summary>
        void FinishUpgrade(string etag);
    }
}
