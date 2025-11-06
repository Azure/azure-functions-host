// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    internal sealed class CustomHandlerHttpOptions
    {
        /// <summary>
        /// Gets or sets the default authorization level for custom handler HTTP worker routes.
        /// </summary>
        public AuthorizationLevel DefaultAuthorizationLevel { get; set; } = AuthorizationLevel.Function;

        /// <summary>
        /// Gets or sets route mapping for a HTTP worker.
        /// </summary>
        public IEnumerable<HttpWorkerRoute> Routes { get; set; }
    }
}
