// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Storage
{
    internal class WebJobsStorageDelegatingHandler : DelegatingHandler
    {
        private const int MaxConnectionsPerServer = 50;

        public WebJobsStorageDelegatingHandler()
        {
            InnerHandler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = MaxConnectionsPerServer
            };
        }
    }
}
