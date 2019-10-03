// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.InteropServices;

namespace Microsoft.Azure.WebJobs.Script.Abstractions
{
    public static class WorkerDescriptionExtensions
    {
        public const string OSPlaceholder = "{os}";
        public const string ArchitecturePlaceholder = "{architecture}";
        public const string RuntimeVersionPlaceholder = "%FUNCTIONS_WORKER_RUNTIME_VERSION%";

        public static void Validate(this WorkerDescription workerDescription)
        {
            if (string.IsNullOrEmpty(workerDescription.Language))
            {
                throw new ValidationException($"WorkerDescription {nameof(workerDescription.Language)} cannot be empty");
            }
            if (workerDescription.Extensions == null)
            {
                throw new ValidationException($"WorkerDescription {nameof(workerDescription.Extensions)} cannot be null");
            }
            if (string.IsNullOrEmpty(workerDescription.DefaultExecutablePath))
            {
                throw new ValidationException($"WorkerDescription {nameof(workerDescription.DefaultExecutablePath)} cannot be empty");
            }
        }

        public static void ValidateWorkerPath(this WorkerDescription description, string workerPath, OSPlatform os, Architecture architecture, string version)
        {
            string language = description.Language;
            if (workerPath.Contains(OSPlaceholder))
            {
                ValidateOSPlatform(description, os);
            }

            if (workerPath.Contains(ArchitecturePlaceholder))
            {
                ValidateArchitecture(description, architecture);
            }

            if (workerPath.Contains(RuntimeVersionPlaceholder) && !string.IsNullOrEmpty(version))
            {
                ValidateRuntimeVersion(description, version);
            }
        }

        private static void ValidateOSPlatform(this WorkerDescription description, OSPlatform os)
        {
            string language = description.Language;
            if (!description.SupportedOperatingSystems.Any(s => s.Equals(os.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new PlatformNotSupportedException($"OS {os.ToString()} is not supported for language {language}");
            }
        }

        private static void ValidateArchitecture(this WorkerDescription description, Architecture architecture)
        {
            string language = description.Language;
            if (!description.SupportedArchitectures.Any(s => s.Equals(architecture.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new PlatformNotSupportedException($"Architecture {architecture.ToString()} is not supported for language {language}");
            }
        }

        private static void ValidateRuntimeVersion(this WorkerDescription description, string version)
        {
            string language = description.Language;
            if (!description.SupportedRuntimeVersions.Any(s => s.Equals(version, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException($"Version {version} is not supported for language {language}");
            }
        }
    }
}
