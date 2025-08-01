// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    public sealed class WorkerConfigurationResolverOptions : IOptionsFormatter
    {
        // Gets or sets the workers directory path within the Host or defined by IConfiguration.
        public string WorkersDirPath { get; set; }

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