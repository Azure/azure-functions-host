// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    /// <summary>
    /// Response payload for the <c>POST /admin/request-slots/leases</c> admin API.
    /// </summary>
    public sealed class RequestSlotsLeaseResponse
    {
        /// <summary>
        /// Gets or sets the number of request slots actually reserved by the runtime.
        /// May be less than <see cref="RequestSlotsLeaseRequest.Count"/> if
        /// insufficient slots were available at the time of the call. The caller is
        /// responsible for releasing whatever was granted via
        /// <c>DELETE /admin/request-slots/leases</c>.
        /// </summary>
        [JsonProperty("acquiredSlotCount")]
        public int AcquiredSlotCount { get; set; }
    }
}
