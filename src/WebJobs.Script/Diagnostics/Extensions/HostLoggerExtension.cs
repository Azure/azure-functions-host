// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;
using static System.Environment;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions
{
    public static class HostLoggerExtension
    {
        // EventId range is 200-299

        private static readonly Action<ILogger, Exception> _hostConfigApplied =
            LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(200, nameof(HostConfigApplied)),
            "Host configuration applied.");

        private static readonly Action<ILogger, string, Exception> _hostConfigReading =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(201, nameof(HostConfigReading)),
            "Reading host configuration file '{hostFilePath}'");

        private static readonly Action<ILogger, string, string, Exception> _hostConfigRead =
            LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(202, nameof(HostConfigRead)),
            "Host configuration file read:{newLine}{sanitizedJson}");

        private static readonly Action<ILogger, string, Exception> _hostConfigEmpty =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(203, nameof(HostConfigEmpty)),
            "Empty host configuration file found. Creating a default {fileName} file.");

        private static readonly Action<ILogger, string, Exception> _hostConfigNotFound =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(204, nameof(HostConfigNotFound)),
            "No host configuration file found. Creating a default {fileName} file.");

        private static readonly Action<ILogger, string, Exception> _hostConfigCreationFailed =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(205, nameof(HostConfigCreationFailed)),
            "Failed to create {fileName} file. Host execution will continue.");

        private static readonly Action<ILogger, string, Exception> _hostConfigFileSystemReadOnly =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(206, nameof(HostConfigFileSystemReadOnly)),
            "File system is read-only. Skipping {fileName} creation.");

        private static readonly Action<ILogger, Exception> _debugerManagerUnableToUpdateSentinelFile =
            LoggerMessage.Define(
            LogLevel.Error,
            new EventId(207, nameof(DebugerManagerUnableToUpdateSentinelFile)),
            "Unable to update the debug sentinel file.");

        public static void HostConfigApplied(this ILogger logger)
        {
            _hostConfigApplied(logger, null);
        }

        public static void HostConfigReading(this ILogger logger, string hostFilePath)
        {
            _hostConfigReading(logger, hostFilePath, null);
        }

        public static void HostConfigRead(this ILogger logger, string sanitizedJson)
        {
            string newLine = NewLine;
            _hostConfigRead(logger, newLine, sanitizedJson, null);
        }

        public static void HostConfigEmpty(this ILogger logger)
        {
            string fileName = ScriptConstants.HostMetadataFileName;
            _hostConfigEmpty(logger, fileName, null);
        }

        public static void HostConfigNotFound(this ILogger logger)
        {
            string fileName = ScriptConstants.HostMetadataFileName;
            _hostConfigNotFound(logger, fileName, null);
        }

        public static void HostConfigCreationFailed(this ILogger logger)
        {
            string fileName = ScriptConstants.HostMetadataFileName;
            _hostConfigCreationFailed(logger, fileName, null);
        }

        public static void HostConfigFileSystemReadOnly(this ILogger logger)
        {
            string fileName = ScriptConstants.HostMetadataFileName;
            _hostConfigFileSystemReadOnly(logger, fileName, null);
        }

        public static void DebugerManagerUnableToUpdateSentinelFile(this ILogger logger, Exception ex)
        {
            _debugerManagerUnableToUpdateSentinelFile(logger, null);
        }
    }
}
