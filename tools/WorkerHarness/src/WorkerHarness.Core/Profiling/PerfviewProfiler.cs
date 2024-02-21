// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace WorkerHarness.Core.Profiling
{
    internal sealed class PerfviewProfiler : IProfiler
    {
        private const string PerfViewExeName = "Perfview.exe";
        private readonly PerfviewConfig _config;
        private readonly string _profilesPath;
        private readonly ILogger<PerfviewProfiler> _logger;
        private string _logFilePath = string.Empty;
        private string _traceDataFilePath = string.Empty;
        private readonly string _executablePath;

        public PerfviewProfiler(PerfviewConfig config, ILogger<PerfviewProfiler> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _profilesPath = _config.ProfilesDirectory ?? _config.ExecutableDirectory;
            _logger = logger;
            _executablePath = Path.Combine(_config.ExecutableDirectory, PerfViewExeName);
        }

        public ProfilingStatus Status { get; private set; }

        public async ValueTask StartProfilingAsync()
        {
            if (Status == ProfilingStatus.Started)
            {
                _logger.LogWarning("Profiling has already started. No action will be taken.");
                return;
            }

            if (!File.Exists(_executablePath))
            {
                _logger.LogWarning($"Perfview executable not found at {_executablePath}.Skipping profiling");
                return;
            }

            Status = ProfilingStatus.Started;

            var timestamp = DateTime.UtcNow.ToString("yyyy_MM_dd_HH_mm_ss");
            _logFilePath = $@"{_profilesPath}\PerfViewLog_{timestamp}.txt";
            _traceDataFilePath = $@"{_profilesPath}\PerfviewData_{timestamp}.etl";

            var circularBufferMb = _config.CircularMb ?? 50;
            var osBufferSizeMb = _config.BufferSizeMb ?? 64;

            _logger.LogInformation($"Starting Perfview profiling...");

            string startArgs =
                $"start /AcceptEula /LogFile={_logFilePath} /BufferSizeMB={osBufferSizeMb} /ThreadTime /CircularMB={circularBufferMb}"
                + " /NoV2Rundown /NoNGENRundown"
                + $" /Providers={_config.Providers} {_traceDataFilePath}";

            var timeout = _config.StartTimeoutInSeconds ?? 5;
            using (var startProcess = new ProcessRunner(TimeSpan.FromSeconds(timeout)))
            {
                await startProcess.Run(_executablePath, startArgs);
                _logger.LogInformation($"Perfview started. Elapsed: {startProcess.ElapsedTime.Seconds} seconds");
            }
        }

        public async ValueTask StopProfilingAsync()
        {
            if (Status == ProfilingStatus.Stopped)
            {
                return;
            }

            _logger.LogInformation($"Stopping Perfview profiling...");

            string stopArgs = $"stop /AcceptEula /LogFile={_logFilePath} /Providers={_config.Providers}"
                              + " /NoV2Rundown /NoNGENRundown /NoNGenPdbs /Merge=true /Zip=false";

            var timeout = _config.StopTimeoutInSeconds ?? (60 * 5);
            using (var stopProcess = new ProcessRunner(TimeSpan.FromSeconds(timeout)))
            {
                await stopProcess.Run(_executablePath, stopArgs);
                _logger.LogInformation($"Perfview stopped. Elapsed: {stopProcess.ElapsedTime.Seconds} seconds. . Profile file:{_traceDataFilePath}");
                stopProcess.Dispose();
            }

            Status = ProfilingStatus.Stopped;
        }
    }
}