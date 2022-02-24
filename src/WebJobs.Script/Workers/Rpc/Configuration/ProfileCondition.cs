// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public enum ConditionType
    {
        [EnumMember(Value = "environment")]
        Environment,
        [EnumMember(Value = "hostProperty")]
        HostProperty
    }

    public enum ConditionHostPropertyName
    {
        [EnumMember(Value = "sku")]
        Sku,
        [EnumMember(Value = "platform")]
        Platform,
        [EnumMember(Value = "hostVersion")]
        HostVersion
    }

    public class ProfileCondition
    {
        /// <summary>
        /// Gets or sets the type of the condition
        /// </summary>
        [JsonProperty(PropertyName = "type")]
        public ConditionType Type { get; set; }

        /// <summary>
        /// Gets or sets the condition name for which expression is applied
        /// </summary>
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the expression for the condition
        /// </summary>
        [JsonProperty(PropertyName = "expression")]
        public string Expression { get; set; }
    }
}