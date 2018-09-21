// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Helpers
{
    public class ArmListEntry<T> where T : INamedObject
    {
        [JsonProperty(PropertyName = "value")]
        public IEnumerable<ArmEntry<T>> Value { get; set; }
    }

    public class ArmEntry<T> where T : INamedObject
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "type")]
        public string Type { get; set; }

        [JsonProperty(PropertyName = "location")]
        public string Location { get; set; }

        [JsonProperty(PropertyName = "properties")]
        public T Properties { get; set; }
    }
}
