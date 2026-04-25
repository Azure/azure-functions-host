// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    /// <summary>
    /// Represents the platform metadata portion of a Legion Goal 3 runtime container
    /// assignment request. Serialized by Legion using System.Text.Json with camelCase
    /// property names; deserialized here with Newtonsoft.Json.
    /// </summary>
    public class RuntimeApiServerAssignmentRequest
    {
        [JsonProperty("appName")]
        public string AppName { get; set; }

        [JsonProperty("siteId")]
        public string SiteId { get; set; }

        [JsonProperty("antaresStampName")]
        public string AntaresStampName { get; set; }

        [JsonProperty("subscriptionId")]
        public string SubscriptionId { get; set; }

        [JsonProperty("functionAppSubscriptionId")]
        public string FunctionAppSubscriptionId { get; set; }

        [JsonProperty("instanceMemory")]
        public int InstanceMemory { get; set; }

        [JsonProperty("poolGroupAllocationLabel")]
        public string PoolGroupAllocationLabel { get; set; }

        [JsonProperty("containerType")]
        public string ContainerType { get; set; }

        [JsonProperty("functionGroupName")]
        public string FunctionGroupName { get; set; }

        [JsonProperty("isAlwaysReadyInstance")]
        public bool IsAlwaysReadyInstance { get; set; }

        [JsonProperty("maxHttpConcurrency")]
        public int MaxHttpConcurrency { get; set; }

        [JsonProperty("configLastModifiedTime")]
        public long ConfigLastModifiedTime { get; set; }

        [JsonProperty("contentLastModifiedTime")]
        public long ContentLastModifiedTime { get; set; }
    }
}
