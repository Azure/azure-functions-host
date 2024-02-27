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
        public string ExecutableDirectory { get; set; } = string.Empty;

        /// <summary>
        /// The directory where the PerfView profiles are stored.
        /// </summary>
        public string? ProfilesDirectory { get; set; }

        /// <summary>
        /// Comma separated list of providers to enable
        /// </summary>
        public string Providers { get; set; } = string.Empty;

        public int? CircularMb { get; set; }

        public int? BufferSizeMb { get; set; }

        public int? StartTimeoutInSeconds { get; set; }

        public int? StopTimeoutInSeconds { get; set; }

        public TraceUploaderConfig? TraceUploaderConfig { get; set; }
    }
}
