// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    public class HttpWorkerOptions
    {
        public CustomHandlerType Type { get; set; }

        public HttpWorkerDescription Description { get; set; }

        public WorkerProcessArguments Arguments { get; set; }

        public int Port { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the host will forward the request to the worker process.
        /// </summary>
        /// <remarks>
        /// The host will rebuild the initial invocation HTTP Request and send the copy to the worker process.
        /// </remarks>
        public bool EnableForwardingHttpRequest { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the host will proxy the invocation HTTP request to the worker process.
        /// </summary>
        public bool EnableProxyingHttpRequest { get; set; }

        public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
