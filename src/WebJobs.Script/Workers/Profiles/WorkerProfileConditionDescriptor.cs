// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Workers.Profiles
{
    public sealed class WorkerProfileConditionDescriptor
    {
        [JsonExtensionData]
#pragma warning disable CS0649 // The value is assigned by the serializer
        private IDictionary<string, JToken> _extensionData;
#pragma warning restore CS0649

        private IDictionary<string, string> _properties;

        [JsonProperty(Required = Required.Always, PropertyName = WorkerConstants.WorkerDescriptionProfileConditionType)]
        public string Type { get; set; }

        public IDictionary<string, string> Properties
        {
            get
            {
                if (_properties == null)
                {
                    _properties = _extensionData?.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()) ?? new Dictionary<string, string>();
                }

                return _properties;
            }
        }
    }
}
