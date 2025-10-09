// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Options that control Script Host recycling behavior.
    /// </summary>
    public sealed class ScriptHostRecycleOptions : IOptionsFormatter
    {
        /// <summary>
        /// Gets or sets a value indicating whether sequential host restarts are required.
        /// </summary>
        public bool SequentialHostRestartRequired { get; set; }

        public static ScriptHostRecycleOptions Create(IConfiguration configuration)
        {
            ScriptHostRecycleOptions options = new();
            options.Configure(configuration);
            return options;
        }

        public string Format()
        {
            return JsonSerializer.Serialize(this, typeof(ScriptHostRecycleOptions), ScriptHostRecycleOptionsJsonContext.Default);
        }

        internal void Configure(IConfiguration configuration)
        {
            var sequentialRestartSetting = configuration.GetSection(ConfigurationSectionNames.SequentialJobHostRestart);
            if (sequentialRestartSetting != null)
            {
                _ = bool.TryParse(sequentialRestartSetting.Value, out bool enforceSequentialOrder);
                SequentialHostRestartRequired = enforceSequentialOrder;
            }
        }
    }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization, WriteIndented = true)]
    [JsonSerializable(typeof(ScriptHostRecycleOptions))]
    internal partial class ScriptHostRecycleOptionsJsonContext : JsonSerializerContext;
}
