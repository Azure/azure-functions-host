// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.Conditions
{
    /// <summary>
    /// Serialized representation of a condition. The <see cref="Type"/> field selects
    /// which condition implementation the provider constructs; any remaining JSON
    /// properties are captured in <see cref="Properties"/> so new condition types
    /// can add their own fields without modifying this DTO.
    /// </summary>
    public sealed class ConditionDescriptor
    {
        [JsonRequired]
        [JsonPropertyName(ConditionConstants.ConditionType)]
        public string Type { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> Properties { get; set; } = new Dictionary<string, JsonElement>();
    }
}
