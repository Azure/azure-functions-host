// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class HostnameFixupMiddleware
    {
        private readonly ILogger _logger;
        private readonly RequestDelegate _next;
        private readonly HostNameProvider _hostNameProvider;
        private readonly IFunctionsSyncManager _functionsSyncManager;

        public HostnameFixupMiddleware(RequestDelegate next, ILogger<HostnameFixupMiddleware> logger, HostNameProvider hostNameProvider, IFunctionsSyncManager functionsSyncManager)
        {
            _logger = logger;
            _next = next;
            _hostNameProvider = hostNameProvider;
            _functionsSyncManager = functionsSyncManager;
        }

        public async Task Invoke(HttpContext context)
        {
            if (_hostNameProvider.Synchronize(context.Request))
            {
                // Hostname has changed - likely a swap has happened (Controller pings us after a swap).
                // We do a sync triggers to ensure we're synced correctly.
                // In slot scenarios the hostname gets out of sync, which can
                // lead to a slot doing a sync for the wrong host with wrong content.
                await _functionsSyncManager.TrySyncTriggersAsync();
            }

            await _next.Invoke(context);
        }
    }
}
