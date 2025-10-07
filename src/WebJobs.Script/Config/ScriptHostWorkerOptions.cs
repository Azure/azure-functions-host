// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Options for worker related behaviors in the Script Host.
    /// </summary>
    public sealed class ScriptHostWorkerOptions : IOptionsFormatter
    {
        /// <summary>
        /// Gets or sets a value indicating whether sequential host restarts are required.
        /// </summary>
        public bool SequentialHostRestartRequired { get; set; }

        public string Format()
        {
            return JsonSerializer.Serialize(this, typeof(ScriptHostWorkerOptions), ScriptHostWorkerOptionsJsonSerializerContext.Default);
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(ScriptHostWorkerOptions))]
    internal partial class ScriptHostWorkerOptionsJsonSerializerContext : JsonSerializerContext;
}
