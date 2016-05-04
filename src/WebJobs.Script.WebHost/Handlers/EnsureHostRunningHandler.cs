// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Handlers
{
    public class EnsureHostRunningHandler : DelegatingHandler
    {
        private readonly TimeSpan _hostTimeout = new TimeSpan(0, 0, 10);
        private readonly int _hostRunningPollIntervalMs = 500;
        private WebScriptHostManager _scriptHostManager;
 
        public EnsureHostRunningHandler(HttpConfiguration config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _scriptHostManager = (WebScriptHostManager)config.DependencyResolver.GetService(typeof(WebScriptHostManager));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // some routes do not require the host to be running (most do)
            bool bypassHostCheck = request.RequestUri.LocalPath.Trim('/').ToLowerInvariant().EndsWith("admin/host/status");

            if (!bypassHostCheck)
            {
                // If the host is not running, we'll wait a bit for it to fully
                // initialize. This might happen if http requests come in while the
                // host is starting up for the first time, or if it is restarting.
                TimeSpan timeWaited = TimeSpan.Zero;
                while (!_scriptHostManager.IsRunning && (timeWaited < _hostTimeout))
                {
                    await Task.Delay(_hostRunningPollIntervalMs);
                    timeWaited += TimeSpan.FromMilliseconds(_hostRunningPollIntervalMs);
                }

                // if the host is not running after or wait time has expired
                // return a 503
                if (!_scriptHostManager.IsRunning)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}