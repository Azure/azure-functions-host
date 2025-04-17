// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.Workers.Profiles
{
    public sealed class WorkerProfileConditionDescriptor
    {
        [JsonRequired]
        [JsonPropertyName(WorkerConstants.WorkerDescriptionProfileConditionType)]
        public string Type { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> Properties { get; set; } = new Dictionary<string, JsonElement>();
    }
}
