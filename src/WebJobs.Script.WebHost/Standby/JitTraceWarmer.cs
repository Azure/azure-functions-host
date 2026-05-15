// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Diagnostics.JitTrace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Standby
{
    /// <summary>
    /// Runs PreJIT against the configured <c>.jittrace</c> files at most once per
    /// process. Used both by <see cref="HostWarmupMiddleware"/> for the AppService
    /// <c>/api/WarmUp</c> trigger path and by <see cref="ComputeSeparationJitWarmupService"/>
    /// for the compute-separation flow where no warmup HTTP call arrives.
    /// </summary>
    public sealed class JitTraceWarmer
    {
        private readonly IEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly ILogger<JitTraceWarmer> _logger;
        private readonly string _assemblyLocalPath;
        private readonly object _gate = new();

        private volatile bool _hasRun;

        public JitTraceWarmer(IEnvironment environment, IConfiguration configuration, ILogger<JitTraceWarmer> logger)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(JitTraceWarmer).Assembly.Location).LocalPath);
        }

        public bool HasRun => _hasRun;

        /// <summary>
        /// Runs PreJIT for the configured trace files. Subsequent calls are no-ops.
        /// </summary>
        public void RunOnce()
        {
            if (_hasRun)
            {
                return;
            }

            lock (_gate)
            {
                if (_hasRun)
                {
                    return;
                }

                foreach (string jitTraceFileName in GetJitTraceFileNames())
                {
                    PreJitPrepare(jitTraceFileName);
                }

                _hasRun = true;
            }
        }

        internal IEnumerable<string> GetJitTraceFileNames()
        {
            yield return WarmUpConstants.JitTraceFileName;

            if (_environment.IsAnyLinuxConsumption())
            {
                yield return WarmUpConstants.LinuxJitTraceFileName;

                if (_configuration.IsExternalWorkerEnabled())
                {
                    yield return WarmUpConstants.LinuxComputeSeparationJitTraceFileName;
                }
            }
        }

        private void PreJitPrepare(string jitTraceFileName)
        {
            string path = Path.Combine(_assemblyLocalPath, WarmUpConstants.PreJitFolderName, jitTraceFileName);
            var file = new FileInfo(path);

            if (!file.Exists)
            {
                return;
            }

            JitTraceRuntime.Prepare(file, out int successfulPrepares, out int failedPrepares);

            // Monitor failed vs successful prepares. A regression in this ratio indicates that
            // the code paths covered by the trace have drifted and the .jittrace files need
            // to be regenerated.
            _logger.LogInformation(
                new EventId(100, "PreJit"),
                "PreJIT Successful prepares: {successfulPrepares}, Failed prepares: {failedPrepares} FileName = {jitTraceFileName}",
                successfulPrepares,
                failedPrepares,
                jitTraceFileName);
        }
    }
}
