// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace WorkerHarness.Core.Profiling
{
    internal sealed class PerfviewProfiler : IProfiler, IDisposable
    {
        private const string PerfViewExeName = "Perfview.exe";
        private readonly PerfviewConfig _config;
        private readonly string _profilesPath;
        private readonly ILogger<PerfviewProfiler> _logger;
        private string _logFilePath = string.Empty;
        private string _traceDataFilePath = string.Empty;
        private ProfilingStatus _profilingStatus;
        private Process? _startProcess;
        private Process? _endProcess;
        private object _lock = new object();

        public PerfviewProfiler(PerfviewConfig config, ILogger<PerfviewProfiler> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _profilesPath = _config.ProfilesDirectory ?? _config.ExecutableDirectory;
            _logger = logger;
        }

        public async ValueTask StartProfilingAsync()
        {
            lock (_lock)
            {
                if (_profilingStatus == ProfilingStatus.Started)
                {
                    _logger.LogWarning("Profiling has already started. No action will be taken.");
                    return;
                }

                var exeFullPath = Path.Combine(_config.ExecutableDirectory, PerfViewExeName);

                if (!File.Exists(exeFullPath))
                {
                    _logger.LogWarning($"Perfview executable not found at {exeFullPath}.Skipping profiling");
                    return;
                }

                _profilingStatus = ProfilingStatus.Started;

                var timestamp = DateTime.UtcNow.ToString("yyyy_MM_dd_HH_mm_ss");
                _logFilePath = $@"{_profilesPath}\PerfViewLog_{timestamp}.txt";
                _traceDataFilePath = $@"{_profilesPath}\PerfviewData_{timestamp}.etl";

                var circularBufferMb = _config.CircularMb ?? 50;
                var osBufferSizeMb = _config.BufferSizeMb ?? 64;

                _logger.LogInformation($"Starting Perfview profiling. This may take a few seconds.");

                string startArgs =
                    $"start /AcceptEula /LogFile={_logFilePath} /BufferSizeMB={osBufferSizeMb} /ThreadTime /CircularMB={circularBufferMb}"
                    + " /NoV2Rundown /NoNGENRundown"
                    + $" /Providers={_config.Providers} {_traceDataFilePath}";

                _startProcess = RunCommand(_config.ExecutableDirectory, PerfViewExeName, startArgs);
            }

            // Give it some time to start, otherwise we will miss some events we write right after this.
            await Task.Delay(5000);
        }

        public void StopProfiling()
        {
            lock (_lock)
            {
                if (_profilingStatus == ProfilingStatus.Stopped)
                {
                    return;
                }

                _logger.LogInformation($"Stopping Perfview profiling. Profile file:{_traceDataFilePath}");

                string stopArgs = $"stop /AcceptEula /LogFile={_logFilePath} /Providers={_config.Providers}"
                                  + " /NoV2Rundown /NoNGENRundown /NoNGenPdbs /Merge=true /Zip=false";

                _endProcess = RunCommand(_config.ExecutableDirectory, PerfViewExeName, stopArgs);
                _profilingStatus = ProfilingStatus.Stopped;
            }
        }

        private Process RunCommand(string workingDirectory, string exePath, string arguments)
        {
            var processStartInfo = new ProcessStartInfo(exePath, arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = workingDirectory
            };

            var process = Process.Start(processStartInfo);
            return process;
        }

        public void Dispose()
        {
            if (_startProcess != null)
            {
                if (!_startProcess.HasExited)
                {
                    _startProcess.Kill();
                    _startProcess.WaitForExit(TimeSpan.FromSeconds(1));
                }

                _startProcess.Dispose();
            }

            if (_endProcess != null)
            {
                if (!_endProcess.HasExited)
                {
                    _endProcess.Kill();
                    _endProcess.WaitForExit(TimeSpan.FromSeconds(2));
                }

                _endProcess.Dispose();
            }
        }
    }
}