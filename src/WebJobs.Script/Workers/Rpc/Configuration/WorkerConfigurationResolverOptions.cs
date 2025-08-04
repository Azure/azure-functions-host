// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    public sealed class WorkerConfigurationResolverOptions : IOptionsFormatter
    {
        // Gets or sets the value of platform release channel.
        public string ReleaseChannel { get; set; }

        // Gets or sets the value of worker runtime.
        public string WorkerRuntime { get; set; }

        // Gets or sets a value indicating whether placeholder mode is enabled.
        public bool IsPlaceholderModeEnabled { get; set; }

        // Gets or sets a value indicating whether it is a multi-language worker environment.
        public bool IsMultiLanguageWorkerEnvironment { get; set; }

        // Gets or sets the workers directory path within the Host.
        public string WorkersDirPath { get; set; }

        // Gets or sets the list of probing paths for worker resolution.
        public List<string> ProbingPaths { get; set; }

        // Gets or sets the worker runtimes available for resolution via Hosting configuration.
        public HashSet<string> WorkersAvailableForResolution { get; set; }

        // Gets or sets the dictionary containing language workers related settings in configuration.
        public Dictionary<string, string> LanguageWorkersSettings { get; set; }

        // Implements the Format method from IOptionsFormatter interface.
        public string Format()
        {
            return JsonSerializer.Serialize(this, typeof(WorkerConfigurationResolverOptions), ConfigResolverOptionsJsonSerializerContext.Default);
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(WorkerConfigurationResolverOptions))]
    internal partial class ConfigResolverOptionsJsonSerializerContext : JsonSerializerContext;
}
