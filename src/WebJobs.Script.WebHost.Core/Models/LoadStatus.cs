// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class LoadStatus
    {
        /// <summary>
        /// Gets or sets a value indicating whether the current host load is high.
        /// </summary>
        [JsonProperty(PropertyName = "isHigh")]
        public bool IsHigh { get; set; }
    }
}