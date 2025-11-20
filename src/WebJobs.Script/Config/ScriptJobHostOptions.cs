// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptJobHostOptions : IOptionsFormatter
    {
        private static readonly JsonSerializerOptions _serializerOptions = new()
        {
            Converters = { new ScriptJobHostOptionsConverter() },
            WriteIndented = true,
        };

        private string _rootScriptPath;
        private ImmutableArray<string> _directorySnapshot;

        public ScriptJobHostOptions()
        {
            FileWatchingEnabled = true;
            FileLoggingMode = FileLoggingMode.Never;
            InstanceId = Guid.NewGuid().ToString();
            WatchDirectories = new Collection<string>();
            WatchFiles = new Collection<string>();
        }

        /// <summary>
        /// Gets or sets the path to the script function directory.
        /// </summary>
        public string RootScriptPath
        {
            get => _rootScriptPath;
            set
            {
                _directorySnapshot = ImmutableArray<string>.Empty;
                _rootScriptPath = value;
            }
        }

        /// <summary>
        /// Gets the current ScriptHost instance id.
        /// </summary>
        public string InstanceId { get; }

        /// <summary>
        /// Gets or sets NugetFallBackPath.
        /// </summary>
        public string NugetFallBackPath { get; set; }

        /// <summary>
        /// Gets or sets the root path for log files.
        /// </summary>
        public string RootLogPath { get; set; }

        /// <summary>
        /// Gets or sets the root path for sample test data.
        /// </summary>
        public string TestDataPath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the <see cref="ScriptHost"/> should
        /// monitor file for changes (default is true). When set to true, the host will
        /// automatically react to source/config file changes. When set to false no file
        /// monitoring will be performed.
        /// </summary>
        public bool FileWatchingEnabled { get; set; }

        /// <summary>
        /// Gets or sets the collection of directories (relative to RootScriptPath) that
        /// should be monitored for changes. If FileWatchingEnabled is true, these directories
        /// will be monitored. When a file is added/modified/deleted in any of these
        /// directories, the host will restart.
        /// </summary>
        public ICollection<string> WatchDirectories { get; set; }

        /// <summary>
        /// Gets or sets the collection of file names that
        /// should be monitored for changes. If FileWatchingEnabled is true, these files
        /// will be monitored. When a file from this list is added/modified/deleted,
        /// the host will restart.
        /// </summary>
        public ICollection<string> WatchFiles { get; set; }

        /// <summary>
        /// Gets or sets a value governing when logs should be written to disk.
        /// When enabled, logs will be written to the directory specified by
        /// <see cref="RootLogPath"/>.
        /// </summary>
        public FileLoggingMode FileLoggingMode { get; set; }

        /// <summary>
        /// Gets or sets the list of functions that should be run. This list can be used to filter
        /// the set of functions that will be enabled - it can be a subset of the actual
        /// function directories. When left null (the default) all discovered functions will
        /// be run.
        /// </summary>
        public ICollection<string> Functions { get; set; }

        /// <summary>
        /// Gets or sets a value indicating the timeout duration for all functions. If null,
        /// there is no timeout duration.
        /// </summary>
        public TimeSpan? FunctionTimeout { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the host is running
        /// outside of the normal Azure hosting environment. E.g. when running
        /// locally or via CLI.
        /// </summary>
        public bool IsSelfHost { get; set; }

        /// <summary>
        /// Gets or sets retry options to use on function executions on function invocation failures.
        /// </summary>
        public RetryOptions Retry { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the filesystem is read-only.
        /// </summary>
        public bool IsFileSystemReadOnly { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the ScriptHost is in standby mode.
        /// </summary>
        public bool IsStandbyConfiguration { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the host sends cancelled invocations to the worker.
        /// This defaults to true, meaning if cancellation is signalled we will still send the pre-cancelled
        /// invocation to the worker.
        /// </summary>
        public bool SendCanceledInvocationsToWorker { get; set; } = true;

        /// <summary>
        /// Gets or sets the telemetry mode.
        /// </summary>
        internal TelemetryMode TelemetryMode { get; set; } = TelemetryMode.ApplicationInsights;

        /// <summary>
        /// Gets or sets a value indicating whether the host.json file was created by the host.
        /// </summary>
        public bool IsDefaultHostConfig { get; set; }

        /// <summary>
        /// Gets or sets a value indicating the timeout duration for the function metadata provider.
        /// </summary>
        public TimeSpan MetadataProviderTimeout { get; set; } = TimeSpan.Zero;

        public string Format()
        {
            return JsonSerializer.Serialize(this, _serializerOptions);
        }

        private class ScriptJobHostOptionsConverter : JsonConverter<ScriptJobHostOptions>
        {
            public override ScriptJobHostOptions Read(
                ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(
                Utf8JsonWriter writer, ScriptJobHostOptions value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteBoolean(nameof(value.FileWatchingEnabled), value.FileWatchingEnabled);
                writer.WriteString(nameof(value.FileLoggingMode), value.FileLoggingMode.ToString());
                writer.WriteString(nameof(value.FunctionTimeout), value.FunctionTimeout?.ToString());
                writer.WriteString(nameof(value.TelemetryMode), value.TelemetryMode.ToString());
                writer.WriteEndObject();
            }
        }
    }
}
