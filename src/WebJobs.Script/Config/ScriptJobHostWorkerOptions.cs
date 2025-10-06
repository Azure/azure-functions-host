// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptJobHostWorkerOptions : IOptionsFormatter
    {
        public bool SequentialHostRestartRequired { get; set; }

        public string Format()
        {
            return JsonSerializer.Serialize(this, typeof(ScriptJobHostWorkerOptions), ScriptJobHostWorkerOptionsJsonSerializerContext.Default);
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(ScriptJobHostWorkerOptions))]
    internal partial class ScriptJobHostWorkerOptionsJsonSerializerContext : JsonSerializerContext;
}
