// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Handlers
{
    public class WebScriptHostHandler : DelegatingHandler
    {
        public const int HostTimeoutSeconds = 30;
        public const int HostPollingIntervalMilliseconds = 25;

        private readonly int _hostTimeoutSeconds;
        private readonly int _hostRunningPollIntervalMilliseconds;
        private readonly HttpConfiguration _config;

        public WebScriptHostHandler(HttpConfiguration config, int hostTimeoutSeconds = HostTimeoutSeconds, int hostPollingIntervalMilliseconds = HostPollingIntervalMilliseconds)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _hostRunningPollIntervalMilliseconds = hostPollingIntervalMilliseconds;
            _hostTimeoutSeconds = hostTimeoutSeconds;
            _config = config;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SetRequestId(request);

            var resolver = _config.DependencyResolver;
            var scriptHostManager = resolver.GetService<WebScriptHostManager>();
            if (!scriptHostManager.Initialized)
            {
                scriptHostManager.Initialize();
            }

            var webHostSettings = resolver.GetService<WebHostSettings>();
            if (webHostSettings.IsAuthDisabled)
            {
                request.SetProperty(ScriptConstants.AzureFunctionsHttpRequestAuthorizationDisabledKey, true);
            }

            // some routes do not require the host to be running (most do)
            bool bypassHostCheck = request.MatchRoute("admin/host/status");
            if (!bypassHostCheck)
            {
                // If the host is not running, we'll wait a bit for it to fully
                // initialize. This might happen if http requests come in while the
                // host is starting up for the first time, or if it is restarting.
                await scriptHostManager.DelayUntilHostReady(_hostTimeoutSeconds, _hostRunningPollIntervalMilliseconds);
            }

            if (StandbyManager.IsWarmUpRequest(request))
            {
                await StandbyManager.WarmUp(request, scriptHostManager);
            }

            return await base.SendAsync(request, cancellationToken);
        }

        internal static void SetRequestId(HttpRequestMessage request)
        {
            string requestID = request.GetHeaderValueOrDefault(ScriptConstants.AntaresLogIdHeaderName) ?? Guid.NewGuid().ToString();
            request.Properties[ScriptConstants.AzureFunctionsRequestIdKey] = requestID;
        }
    }
}