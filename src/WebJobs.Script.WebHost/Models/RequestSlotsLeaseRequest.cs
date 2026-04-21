// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    /// <summary>
    /// Represents a request to modify the number of leased request slots for the current instance.
    /// </summary>
    public sealed class RequestSlotsLeaseRequest
    {
        /// <summary>
        /// Gets or sets the number of request slots to acquire or release.
        /// Must be greater than zero.
        /// </summary>
        [JsonProperty("count")]
        public int Count { get; set; }
    }
}
