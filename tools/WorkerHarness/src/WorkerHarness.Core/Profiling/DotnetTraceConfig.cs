// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Profiling
{
    public sealed class DotnetTraceConfig
    {
        public string? ProfilesDirectory { get; set; }
        public string? Providers { get; set; }

        public TraceUploaderConfig? TraceUploaderConfig { get; set; } = new TraceUploaderConfig();
        public int? DurationInSeconds { get; set; }
    }
}
