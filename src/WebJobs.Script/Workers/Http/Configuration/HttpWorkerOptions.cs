// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    public class HttpWorkerOptions
    {
        public string Type { get; set; } = "http";

        public HttpWorkerDescription Description { get; set; }

        public WorkerProcessArguments Arguments { get; set; }

        public int Port { get; set; }

        public bool EnableForwardingHttpRequest { get; set; }
    }
}
