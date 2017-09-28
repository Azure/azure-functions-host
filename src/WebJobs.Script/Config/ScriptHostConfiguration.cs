// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Extensibility;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptHostConfiguration
    {
        public ScriptHostConfiguration()
        {
            HostConfig = new JobHostConfiguration();
            FileWatchingEnabled = true;
            FileLoggingMode = FileLoggingMode.Never;
            RootScriptPath = Environment.CurrentDirectory;
            RootLogPath = Path.Combine(Path.GetTempPath(), "Functions");
            LogFilter = new LogCategoryFilter();
        }

        /// <summary>
        /// Gets or sets the <see cref="JobHostConfiguration"/>.
        /// </summary>
        public JobHostConfiguration HostConfig { get; set; }

        /// <summary>
        /// Gets or sets the path to the script function directory.
        /// </summary>
        public string RootScriptPath { get; set; }

        /// <summary>
        /// Gets or sets the root path for log files.
        /// </summary>
        public string RootLogPath { get; set; }

        /// <summary>
        /// Gets or sets the custom TraceWriter to add to the trace pipeline
        /// </summary>
        public TraceWriter TraceWriter { get; set; }

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
        /// Gets the set of <see cref="ScriptBindingProviders"/> to use when loading functions.
        /// </summary>
        public ICollection<ScriptBindingProvider> BindingProviders { get; internal set; }

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
        /// Gets or sets the <see cref="LogCategoryFilter"/> to use when constructing providers for the
        /// registered <see cref="ILoggerFactory"/>.
        /// </summary>
        public LogCategoryFilter LogFilter { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="SamplingPercentageEstimatorSettings"/> to be used for Application
        /// Insights client-side sampling. If null, client-side sampling is disabled.
        /// </summary>
        // TODO: FACAVAL
        // Need AI support to re-enable this
        // public SamplingPercentageEstimatorSettings ApplicationInsightsSamplingSettings { get; set; }

        /// <summary>
        /// Gets or sets a test hook for modifying the configuration after host.json has been processed.
        /// </summary>
        internal Action<ScriptHostConfiguration> OnConfigurationApplied { get; set; }
    }
}
