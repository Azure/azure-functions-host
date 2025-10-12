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
        public IReadOnlyList<string> ProbingPaths { get; set; }

        /// <summary>
        /// Gets or sets the worker runtimes available for resolution via Hosting configuration.
        /// </summary>
        public IReadOnlySet<string> WorkersAvailableForResolution { get; set; }

        /// <summary>
        /// Gets or sets the dictionary containing language workers related overrides in configuration.
        /// </summary>
        public IReadOnlyDictionary<string, RpcWorkerDescription> WorkerDescriptionOverrides { get; set; }

        /// <summary>
        /// Gets or sets the dictionary that contains the versions of language workers to be ignored during probing outside of the Host.
        /// Key: worker name (e.g. "node", "python"). Value: set of versions to exclude from consideration.
        /// </summary>
        public IReadOnlyDictionary<string, HashSet<Version>> IgnoredWorkerVersions { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether dynamic worker resolution is enabled.
        /// </summary>
        public bool IsDynamicWorkerResolutionEnabled { get; set; }

        /// <summary>
        /// Gets or sets the workers directory path within the Host or defined by IConfiguration.
        /// </summary>
        public string WorkersRootDirPath { get; set; }

        /// <summary>
        /// Gets or sets the value of processor cores count.
        /// </summary>
        public int EffectiveCoresCount { get; set; }

        /// <summary>
        /// Gets or sets the value of worker process count.
        /// </summary>
        public string WorkerProcessCount { get; set; }

        /// <summary>
        /// Gets or sets the value of function worker runtime version.
        /// </summary>
        public string FunctionsWorkerRuntimeVersion { get; set; }

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
