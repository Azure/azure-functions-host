// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace WorkerHarness.Core.Profiling
{
    public sealed class ProfilerFactory : IProfilerFactory
    {
        private readonly PerfviewConfig _perfviewConfig;
        private readonly ILoggerFactory _loggerFactory;
        public ProfilerFactory(PerfviewConfig perfviewConfig, ILoggerFactory loggerFactory)
        {
            this._perfviewConfig = perfviewConfig;
            _loggerFactory = loggerFactory;
        }

        public IProfiler? CreateProfiler()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var profiler = new PerfviewProfiler(_perfviewConfig, _loggerFactory.CreateLogger<PerfviewProfiler>());
                return profiler;
            }

            return default;
        }
    }
}
