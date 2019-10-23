// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.OutOfProc;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Rpc
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

        public override void ApplyDefaultsAndValidate(string workerDirectory)
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
            if (!string.IsNullOrEmpty(DefaultWorkerPath) && !File.Exists(DefaultWorkerPath))
            {
                throw new FileNotFoundException($"Did not find {nameof(DefaultWorkerPath)} for language: {Language}");
            }

            ResolveDotNetDefaultExecutablePath();
        }

        public void ValidateWorkerPath(string workerPath, OSPlatform os, Architecture architecture, string version)
        {
            if (workerPath.Contains(LanguageWorkerConstants.OSPlaceholder))
            {
                ValidateOSPlatform(os);
            }

            if (workerPath.Contains(LanguageWorkerConstants.ArchitecturePlaceholder))
            {
                ValidateArchitecture(architecture);
            }

            if (workerPath.Contains(LanguageWorkerConstants.RuntimeVersionPlaceholder) && !string.IsNullOrEmpty(version))
            {
                ValidateRuntimeVersion(version);
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

        private void ValidateRuntimeVersion(string version)
        {
            if (!SupportedRuntimeVersions.Any(s => s.Equals(version, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException($"Version {version} is not supported for language {Language}");
            }
        }

        private void ResolveDotNetDefaultExecutablePath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && (DefaultExecutablePath.Equals(LanguageWorkerConstants.DotNetExecutableName, StringComparison.OrdinalIgnoreCase)
                    || DefaultExecutablePath.Equals(LanguageWorkerConstants.DotNetExecutableNameWithExtension, StringComparison.OrdinalIgnoreCase)))
            {
                var programFilesFolder = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                DefaultExecutablePath = Path.Combine(programFilesFolder, LanguageWorkerConstants.DotNetFolderName, DefaultExecutablePath);
            }
        }
    }
}