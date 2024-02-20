// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Profiling
{
    /// <summary>
    /// Configuration for PerfView
    /// </summary>
    public sealed class PerfviewConfig
    {
        /// <summary>
        /// The directory where the PerfView executable is located.
        /// </summary>
        public required string ExecutableDirectory { get; set; }

        /// <summary>
        /// The directory where the PerfView profiles are stored.
        /// </summary>
        public string? ProfilesDirectory { get; set; }

        /// <summary>
        /// Comma separated list of providers to enable
        /// </summary>
        public required string Providers { get; set; }
        
        public int? CircularMb { get; set; }

        public int? BufferSizeMb { get; set; }

        public int? GraceTimeInSecondsAfterStopping { get; set; }

        public int? WaitTimeInSecondsAfterStarting { get; set; }

    }
}
