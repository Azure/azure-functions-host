// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace WorkerHarness.Core.Profiling
{
    internal static class TraceFileUploader
    {
        internal static async ValueTask Upload(TraceUploaderConfig config, ILogger logger, string filePath)
        {
            if (config == null)
            {
                return;
            }

            var uploadDestinationContainerUrl = config.UploadContainerUrl;
            if (string.IsNullOrEmpty(uploadDestinationContainerUrl))
            {
                logger.LogWarning("UploadContainerUrl is not set. Skipping upload of profile data.");
                return;
            }

            config.ExecutableDirectory = config.ExecutableDirectory!.Replace("\\", Path.DirectorySeparatorChar.ToString());
            config.ExecutableDirectory = Path.GetFullPath(config.ExecutableDirectory!);
            logger.LogInformation($"AzCopy executable directory: {config.ExecutableDirectory}");
            var absoluteAzCopyExecutablePath = Path.GetFullPath(config.ExecutableDirectory);
            logger.LogInformation($"AzCopy executable directory: {absoluteAzCopyExecutablePath}");

            var executableName = OperatingSystem.IsWindows() ? "azcopy.exe" : "azcopy";

            var azCopyExecutablePath = Path.Combine(absoluteAzCopyExecutablePath, executableName);
            if (File.Exists(azCopyExecutablePath) == false)
            {
                logger.LogWarning($"AzCopy executable not found at {azCopyExecutablePath}.Skipping upload of profile data.");
                return;
            }

            if (File.Exists(filePath) == false)
            {
                logger.LogWarning($"Profile file not found at {filePath}.Skipping upload of profile data.");
                return;
            }

            var uploadDestinationUrl = GetDestinationUrl(filePath, uploadDestinationContainerUrl);
            string args = $"copy \"{filePath}\" \"{uploadDestinationUrl}\"";

            logger.LogInformation($"{azCopyExecutablePath} {args}");

            using (var uploadProcess = new ProcessRunner(TimeSpan.FromSeconds(60), logger))
            {
                await uploadProcess.Run(azCopyExecutablePath, args);
                logger.LogInformation($"Uploaded profile file. Elapsed: {uploadProcess.ElapsedTime.Seconds} seconds. File:{filePath}");
            }
        }

        private static string GetDestinationUrl(string traceFilePath, string uploadDestinationContainerUrl)
        {
            var fileName = Path.GetFileName(traceFilePath);

            Uri uri = new Uri(uploadDestinationContainerUrl);
            var newUrl = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}/{fileName}{uri.Query}";

            return newUrl;
        }
    }
}