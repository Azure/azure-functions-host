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

        // This is the legacy setting that rebuilds the HTTP request for forwrding
        public bool EnableForwardingHttpRequest { get; set; }

        // This uses YARP to forward the HTTP request
        public bool EnableProxyingHttpRequest { get; set; }

        public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
