// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class GetTriggersResult : TriggersOperationResult
    {
        /// <summary>
        /// Gets or sets the triggers payload.
        /// </summary>
        public string Content { get; set; }
    }
}