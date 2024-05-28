// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using WorkerHarness.Core.Logging;

namespace WorkerHarness.Core.Profiling
{
    internal sealed class PerfviewProfiler : IProfiler
    {
        private const string FunctionsColdStartProfileAnalyzerExeName = "FunctionsColdStartProfileAnalyzer.exe";
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
            var osBufferSizeMb = _config.BufferSizeMb ?? 1024;

            _logger.LogInfo(ConsoleColor.Gray, $"Starting Perfview profiling...");

            string startArgs =
                $"start /AcceptEula /LogFile={_logFilePath} /BufferSizeMB={osBufferSizeMb} /ThreadTime /NoV2Rundown /NoNGENRundown /Providers={_config.Providers} {_traceDataFilePath}";

            var timeout = _config.StartTimeoutInSeconds ?? 5;
            using (var startProcess = new ProcessRunner(TimeSpan.FromSeconds(timeout)))
            {
                await startProcess.Run(_executablePath, startArgs);
                _logger.LogInfo(ConsoleColor.Gray, $"Perfview started. Elapsed: {startProcess.ElapsedTime.Seconds} seconds");
            }
        }

        public async ValueTask StopProfilingAsync()
        {
            if (Status == ProfilingStatus.Stopped)
            {
                return;
            }

            _logger.LogInfo(ConsoleColor.Gray, $"Stopping Perfview profiling. This process may take a few seconds to a few minutes.");

            string stopArgs =
                $"stop /AcceptEula /LogFile={_logFilePath} /Providers={_config.Providers} /NoV2Rundown /NoNGENRundown /NoNGenPdbs /Merge=true /Zip=false";

            var timeout = 120;
            using (var stopProcess = new ProcessRunner(TimeSpan.FromSeconds(timeout)))
            {
                await stopProcess.Run(_executablePath, stopArgs);
                _logger.LogInfo(ConsoleColor.White, $"Perfview stopped. Elapsed time: {stopProcess.ElapsedTime.Seconds} seconds. . Profile file:{_traceDataFilePath}");
            }

            //await UploadProfileToStorageContainer();
            await RunProfileAnalyzer();

            Status = ProfilingStatus.Stopped;
        }

        private async ValueTask RunProfileAnalyzer()
        {
            string absoluteProfileAnalyzerPath = string.Empty;
            string traceDataFileAbsolutePath = string.Empty;
            try
            {
                var profileAnalyzerPath = Path.Combine(_config.ExecutableDirectory, FunctionsColdStartProfileAnalyzerExeName);
                absoluteProfileAnalyzerPath = Path.GetFullPath(profileAnalyzerPath);
                if (File.Exists(absoluteProfileAnalyzerPath) == false)
                {
                    _logger.LogWarning($"Profile analyzer executable not found at {profileAnalyzerPath}.Skipping profile analysis.");
                    return;
                }

                if (File.Exists(_traceDataFilePath) == false)
                {
                    _logger.LogWarning($"Perfview data file not found at {_traceDataFilePath}.Skipping profile analysis.");
                    return;
                }

                traceDataFileAbsolutePath = Path.GetFullPath(_traceDataFilePath);

                var profileAnalyzerArgs = $"{traceDataFileAbsolutePath} {_config.ProfileAnalyzerArguments}";
                _logger.LogInfo(ConsoleColor.Gray, $"Found '{FunctionsColdStartProfileAnalyzerExeName}' present in 'executableDirectory'({_config.ExecutableDirectory}). Will proceed with analyzing the perfview profile to generate coldstart data.");
                _logger.LogInfo(ConsoleColor.Gray, $"Executing {absoluteProfileAnalyzerPath} {profileAnalyzerArgs}");

                using (var profileAnalyzerProcess = new ProcessRunner(TimeSpan.FromSeconds(60)))
                {
                    await profileAnalyzerProcess.Run(absoluteProfileAnalyzerPath, profileAnalyzerArgs);
                    _logger.LogInfo(ConsoleColor.White, $"Profile analysis completed. Elapsed: {profileAnalyzerProcess.ElapsedTime.Seconds} seconds. . Profile file:{traceDataFileAbsolutePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error occurred while running profile analyzer. Profile file:{traceDataFileAbsolutePath}, Profile analyzer path:{absoluteProfileAnalyzerPath}");
            }
        }

        private async ValueTask UploadProfileToStorageContainer()
        {
            var uploadDestinationContainerUrl = _config.UploadContainerUrl;
            if (string.IsNullOrEmpty(uploadDestinationContainerUrl))
            {
                _logger.LogWarning("UploadContainerUrl is not set. Skipping upload of profile data.");
                return;
            }

            var azCopyExecutablePath = Path.Combine(_config.ExecutableDirectory, "AzCopy.exe");
            if (File.Exists(azCopyExecutablePath) == false)
            {
                _logger.LogWarning($"AzCopy executable not found at {azCopyExecutablePath}.Skipping upload of profile data.");
                return;
            }

            if (File.Exists(_traceDataFilePath) == false)
            {
                _logger.LogWarning($"Profile file not found at {_traceDataFilePath}.Skipping upload of profile data.");
                return;
            }

            var uploadDestinationUrl = GetDestinationUrl(_traceDataFilePath, uploadDestinationContainerUrl);
            string args = $"copy \"{_traceDataFilePath}\" \"{uploadDestinationUrl}\"";

            _logger.LogInformation($"{azCopyExecutablePath} {args}");

            using (var uploadProcess = new ProcessRunner(TimeSpan.FromSeconds(60)))
            {
                await uploadProcess.Run(azCopyExecutablePath, args);
                _logger.LogInformation($"Uploaded profile file. Elapsed: {uploadProcess.ElapsedTime.Seconds} seconds. . Profile file:{_traceDataFilePath}");
            }
        }

        private string GetDestinationUrl(string traceFilePath, string uploadDestinationContainerUrl)
        {
            var fileName = Path.GetFileName(traceFilePath);

            Uri uri = new Uri(uploadDestinationContainerUrl);
            var newUrl = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}/{fileName}{uri.Query}";

            return newUrl;
        }
    }
}