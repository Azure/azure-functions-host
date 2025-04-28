// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public sealed class TriggersResult : TriggersOperationResult
    {
        /// <summary>
        /// Gets or sets the triggers payload.
        /// </summary>
        /// <remarks>
        /// Use of an already formatted JSON string here as opposed to a JObject is by design.
        /// We want to ensure that the result we return here is the exact same JSON payload
        /// as is generated during a sync triggers operation (down to whitespacing).
        /// </remarks>
        public string Content { get; set; }
    }
}