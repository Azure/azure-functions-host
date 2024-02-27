// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace WorkerHarness.Core.Profiling.DotnetTrace
{
    internal sealed class DotnetTraceProfiler : IProfiler
    {
        private const string ExecutableName = "dotnet-trace";
        private readonly ILogger<DotnetTraceProfiler> _logger;
        private readonly DotnetTraceConfig _config;
        private ProfilingStatus _status;
        private string _traceDataFilePath;

        public DotnetTraceProfiler(DotnetTraceConfig config, ILogger<DotnetTraceProfiler> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _status = ProfilingStatus.Stopped;
        }

        ProfilingStatus IProfiler.Status => _status;

        public async ValueTask StartProfilingAsync()
        {
            _status = ProfilingStatus.Started;

            _traceDataFilePath = GetTraceFilePath();
            int duration = _config.DurationInSeconds ?? 10;
            var arguments = $"dotnet-trace collect --process-id {Environment.ProcessId} --providers {_config.Providers} --duration 00:00:00:{duration} --output {_traceDataFilePath}";

            var process = new ProcessRunner(TimeSpan.FromSeconds(2), _logger);
            _logger.LogInformation($"Starting dotnet trace profiling for {Environment.ProcessId}. Trace file path:{_traceDataFilePath}");

            await process.Run(ExecutableName, arguments);
        }

        private string GetTraceFilePath()
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy_MM_dd_HH_mm_ss");
            var profilesDirectory = _config.ProfilesDirectory ?? Directory.GetCurrentDirectory();
            profilesDirectory = profilesDirectory.Replace("\\", Path.DirectorySeparatorChar.ToString());
            profilesDirectory = Path.GetFullPath(profilesDirectory);
            var traceDataFilePath = Path.Combine(profilesDirectory, $"trace_{timestamp}.nettrace");

            return traceDataFilePath;
        }

        public async ValueTask StopProfilingAsync()
        {
            // Does not really do anything, as the tracing will be stopped using "--stopping-event-event-name" argument.            
            _status = ProfilingStatus.Stopped;

            if (_config.TraceUploaderConfig != null)
            {
                _logger.LogInformation($"Waiting for {_config.TraceUploaderConfig.WaitTimeInSeconds} seconds to allow trace file to be written to disk");
                await Task.Delay(TimeSpan.FromSeconds(_config.TraceUploaderConfig.WaitTimeInSeconds)); 
                _logger.LogInformation("Uploading trace data to storage account");
                await TraceFileUploader.Upload(_config.TraceUploaderConfig, _logger, _traceDataFilePath);
            }
        }
    }
}
