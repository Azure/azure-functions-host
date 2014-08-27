// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    /// <summary>Defines a reader that provides dashbard version information.</summary>
    public interface IDashboardVersionManager
    {
        /// <summary>Reads the data dashboard version.</summary>
        /// <returns>The dashboard version.</returns>
        DashboardVersion Read();

        /// <summary>Start the process of deleting old statistics.</summary>
        /// <returns>The dashboard version.</returns>
        DashboardVersion StartDeletingOldData(DashboardVersion previousVersion);

        /// <summary>Start the process of restoring the archive.</summary>
        /// <returns>The dashboard version.</returns>
        DashboardVersion StartRestoringArchive(DashboardVersion previousVersion);

        /// <summary>Finishes the upgrade process.</summary>
        /// <returns>The dashboard version.</returns>
        DashboardVersion FinishUpgrade(DashboardVersion previousVersion);
    }
}
