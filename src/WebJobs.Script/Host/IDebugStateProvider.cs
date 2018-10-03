// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Interface for a service providing real time debug/diagnostic mode
    /// notifications and status.
    /// </summary>
    public interface IDebugStateProvider
    {
        /// <summary>
        /// Gets or sets the last time the host has received a debug notification
        /// </summary>
        DateTime LastDebugNotify { get; set; }

        /// <summary>
        /// Gets a value indicating whether the host is in diagnostic mode.
        /// </summary>
        bool InDebugMode { get; }

        /// <summary>
        /// Gets or sets the last time the host has received a diagnostic notification
        /// </summary>
        DateTime LastDiagnosticNotify { get; set; }

        /// <summary>
        /// Gets a value indicating whether the host is in debug mode.
        /// </summary>
        bool InDiagnosticMode { get; }
    }
}
