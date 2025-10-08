// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;

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

        public string Format()
        {
            return JsonSerializer.Serialize(this, typeof(ScriptHostRecycleOptions), ScriptHostRecycleOptionsJsonContext.Default);
        }
    }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization, WriteIndented = true)]
    [JsonSerializable(typeof(ScriptHostRecycleOptions))]
    internal partial class ScriptHostRecycleOptionsJsonContext : JsonSerializerContext;
}
