// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public class RpcWorkerDescription : WorkerDescription
    {
        private List<string> _extensions = new List<string>();

        /// <summary>
        /// Gets or sets the name of the supported language. This is the same name as the IConfiguration section for the worker.
        /// </summary>
        [JsonProperty(PropertyName = "language")]
        public string Language { get; set; }

        /// <summary>
        /// Gets or sets the default runtime version.
        /// </summary>
        [JsonProperty(PropertyName = "defaultRuntimeVersion")]
        public string DefaultRuntimeVersion { get; set; }

        /// <summary>
        /// Gets or sets the supported architectures for this runtime.
        /// </summary>
        [JsonProperty(PropertyName = "supportedArchitectures")]
        public List<string> SupportedArchitectures { get; set; }

        /// <summary>
        /// Gets or sets the supported operating systems for this runtime.
        /// </summary>
        [JsonProperty(PropertyName = "supportedOperatingSystems")]
        public List<string> SupportedOperatingSystems { get; set; }

        /// <summary>
        /// Gets or sets the supported versions for this runtime.
        /// </summary>
        [JsonProperty(PropertyName = "supportedRuntimeVersions")]
        public List<string> SupportedRuntimeVersions { get; set; }

        /// <summary>
        /// Gets or sets the regex used for sanitizing the runtime version string.
        /// </summary>
        [JsonProperty(PropertyName = "sanitizeRuntimeVersionRegex")]
        public string SanitizeRuntimeVersionRegex { get; set; }

        /// <summary>
        /// Gets or sets the supported file extension type. Functions are registered with workers based on extension.
        /// </summary>
        [JsonProperty(PropertyName = "extensions")]
        public List<string> Extensions
        {
            get
            {
                return _extensions;
            }

            set
            {
                if (value != null)
                {
                    _extensions = value;
                }
            }
        }

        public override void ApplyDefaultsAndValidate(string workerDirectory, ILogger logger)
        {
            if (workerDirectory == null)
            {
                throw new ArgumentNullException(nameof(workerDirectory));
            }
            Arguments = Arguments ?? new List<string>();
            WorkerDirectory = WorkerDirectory ?? workerDirectory;
            if (!string.IsNullOrEmpty(DefaultWorkerPath) && !Path.IsPathRooted(DefaultWorkerPath))
            {
                DefaultWorkerPath = Path.Combine(WorkerDirectory, DefaultWorkerPath);
            }
            if (string.IsNullOrEmpty(Language))
            {
                throw new ValidationException($"WorkerDescription {nameof(Language)} cannot be empty");
            }
            if (Extensions == null)
            {
                throw new ValidationException($"WorkerDescription {nameof(Extensions)} cannot be null");
            }
            if (string.IsNullOrEmpty(DefaultExecutablePath))
            {
                throw new ValidationException($"WorkerDescription {nameof(DefaultExecutablePath)} cannot be empty");
            }

            ResolveDotNetDefaultExecutablePath(logger);
        }

        internal void ValidateDefaultWorkerPathFormatters(ISystemRuntimeInformation systemRuntimeInformation)
        {
            if (DefaultWorkerPath.Contains(RpcWorkerConstants.OSPlaceholder))
            {
                ValidateOSPlatform(systemRuntimeInformation.GetOSPlatform());
            }

            if (DefaultWorkerPath.Contains(RpcWorkerConstants.ArchitecturePlaceholder))
            {
                ValidateArchitecture(systemRuntimeInformation.GetOSArchitecture());
            }

            if (DefaultWorkerPath.Contains(RpcWorkerConstants.RuntimeVersionPlaceholder) && !string.IsNullOrEmpty(DefaultRuntimeVersion))
            {
                ValidateRuntimeVersion();
            }
        }

        private void ValidateOSPlatform(OSPlatform os)
        {
            if (!SupportedOperatingSystems.Any(s => s.Equals(os.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new PlatformNotSupportedException($"OS {os.ToString()} is not supported for language {Language}");
            }
        }

        private void ValidateArchitecture(Architecture architecture)
        {
            if (!SupportedArchitectures.Any(s => s.Equals(architecture.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new PlatformNotSupportedException($"Architecture {architecture.ToString()} is not supported for language {Language}");
            }
        }

        private void ValidateRuntimeVersion()
        {
            if (!SupportedRuntimeVersions.Any(s => s.Equals(DefaultRuntimeVersion, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException($"Version {DefaultRuntimeVersion} is not supported for language {Language}");
            }
        }

        private void ResolveDotNetDefaultExecutablePath(ILogger logger)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && (DefaultExecutablePath.Equals(RpcWorkerConstants.DotNetExecutableName, StringComparison.OrdinalIgnoreCase)
                    || DefaultExecutablePath.Equals(RpcWorkerConstants.DotNetExecutableNameWithExtension, StringComparison.OrdinalIgnoreCase)))
            {
                var programFilesFolder = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                var fullPath = Path.Combine(
                                    programFilesFolder,
                                    RpcWorkerConstants.DotNetFolderName,
                                    RpcWorkerConstants.DotNetExecutableNameWithExtension);

                if (FileExists(fullPath))
                {
                    DefaultExecutablePath = fullPath;
                }
                else
                {
                    logger.Log(
                        LogLevel.Warning,
                        $"File '{fullPath}' is not found, '{DefaultExecutablePath}' invocation will rely on the PATH environment variable.");
                }
            }
        }

        internal void FormatWorkerPathIfNeeded(ISystemRuntimeInformation systemRuntimeInformation, IEnvironment environment, ILogger logger)
        {
            if (string.IsNullOrEmpty(DefaultWorkerPath))
            {
                return;
            }

            OSPlatform os = systemRuntimeInformation.GetOSPlatform();
            Architecture architecture = systemRuntimeInformation.GetOSArchitecture();
            string version = environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName);
            logger.LogDebug($"EnvironmentVariable {RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName}: {version}");

            if (!string.IsNullOrEmpty(version))
            {
                DefaultRuntimeVersion = GetSanitizedRuntimeVersion(version);
            }

            ValidateDefaultWorkerPathFormatters(systemRuntimeInformation);

            DefaultWorkerPath = DefaultWorkerPath.Replace(RpcWorkerConstants.OSPlaceholder, os.ToString())
                             .Replace(RpcWorkerConstants.ArchitecturePlaceholder, architecture.ToString())
                             .Replace(RpcWorkerConstants.RuntimeVersionPlaceholder, DefaultRuntimeVersion);
        }

        internal bool ShouldFormatWorkerPath(string workerPath)
        {
            if (string.IsNullOrEmpty(workerPath))
            {
                return false;
            }
            return workerPath.Contains(RpcWorkerConstants.OSPlaceholder) ||
                    workerPath.Contains(RpcWorkerConstants.ArchitecturePlaceholder) ||
                    workerPath.Contains(RpcWorkerConstants.RuntimeVersionPlaceholder);
        }

        private string GetSanitizedRuntimeVersion(string version)
        {
            if (string.IsNullOrEmpty(SanitizeRuntimeVersionRegex))
            {
                return version;
            }

            var match = new Regex(SanitizeRuntimeVersionRegex).Match(version);
            if (!match.Success)
            {
                throw new NotSupportedException($"Version {version} for language {Language} does not match the regular expression '{SanitizeRuntimeVersionRegex}'");
            }

            return match.Value;
        }
    }
}