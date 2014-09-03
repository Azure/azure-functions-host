// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    /// <summary>Defines a manager that manages dashboard version information.</summary>
    public interface IDashboardVersionManager
    {
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
