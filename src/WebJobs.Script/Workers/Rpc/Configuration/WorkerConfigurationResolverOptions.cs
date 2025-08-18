// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    public sealed class WorkerConfigurationResolverOptions : IOptionsFormatter
    {
        /// <summary>
        /// Gets or sets the value of platform release channel.
        /// </summary>
        public string ReleaseChannel { get; set; }

        /// <summary>
        /// Gets or sets the value of worker runtime.
        /// </summary>
        public string WorkerRuntime { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether placeholder mode is enabled.
        /// </summary>
        public bool IsPlaceholderModeEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether it is a multi-language worker environment.
        /// </summary>
        public bool IsMultiLanguageWorkerEnvironment { get; set; }

        /// <summary>
        /// Gets or sets the list of probing paths for worker resolution.
        /// </summary>
        public List<string> ProbingPaths { get; set; }

        /// <summary>
        /// Gets or sets the worker runtimes available for resolution via Hosting configuration.
        /// </summary>
        public HashSet<string> WorkersAvailableForResolution { get; set; }

        /// <summary>
        /// Gets or sets the dictionary containing language workers related settings in configuration.
        /// </summary>
        public Dictionary<string, string> LanguageWorkersSettings { get; set; }

        /// <summary>
        /// Gets or sets the dictionary containing language workers related settings in configuration.
        /// </summary>
        public Dictionary<string, HashSet<Version>> IgnoreWorkerVersions { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether dynamic worker resolution is enabled.
        /// </summary>
        public bool IsDynamicWorkerResolutionEnabled { get; set; }

        /// <summary>
        /// Gets or sets the workers directory path within the Host or defined by IConfiguration.
        /// </summary>
        public string WorkersRootDirPath { get; set; }

        /// <inheritdoc>
        /// Implements the Format method from IOptionsFormatter interface.
        /// </inheritdoc>
        public string Format()
        {
            return JsonSerializer.Serialize(this, typeof(WorkerConfigurationResolverOptions), ConfigResolverOptionsJsonSerializerContext.Default);
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Serialization)]
    [JsonSerializable(typeof(WorkerConfigurationResolverOptions))]
    internal partial class ConfigResolverOptionsJsonSerializerContext : JsonSerializerContext;
}
