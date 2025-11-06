// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Extensions.Http;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    /// <summary>
    /// Route mapping for a custom handler HTTP worker.
    /// </summary>
    internal sealed class HttpWorkerRoute
    {
        /// <summary>
        /// Gets or sets route template.
        /// </summary>
        public string Route { get; set; }

        /// <summary>
        /// Gets or sets the authorization level.
        /// </summary>
        public AuthorizationLevel? AuthorizationLevel { get; set; }
    }
}
