// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    public sealed class WorkerConfigurationResolverOptions : IOptionsFormatter
    {
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
