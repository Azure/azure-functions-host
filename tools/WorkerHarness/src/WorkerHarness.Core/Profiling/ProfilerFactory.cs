// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace WorkerHarness.Core.Profiling
{
    public sealed class ProfilerFactory : IProfilerFactory
    {
        private PerfviewConfig perfviewConfig;
        private ILoggerFactory _loggerFactory;
        public ProfilerFactory(PerfviewConfig perfviewConfig, ILoggerFactory loggerFactory)
        {
            this.perfviewConfig = perfviewConfig;
            _loggerFactory = loggerFactory;
        }

        public IProfiler? CreateProfiler()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var profiler = new PerfviewProfiler(perfviewConfig, _loggerFactory.CreateLogger<PerfviewProfiler>());
                return profiler;
            }

            return default;
        }
    }
}
