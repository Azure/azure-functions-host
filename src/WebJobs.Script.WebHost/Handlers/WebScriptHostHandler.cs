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
        private readonly HttpConfiguration _config;

        public WebScriptHostHandler(HttpConfiguration config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _config = config;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SetRequestId(request);

            var resolver = _config.DependencyResolver;
            var scriptHostManager = resolver.GetService<WebScriptHostManager>();
            if (!scriptHostManager.Initialized)
            {
                // need to ensure the host manager is initialized early in the pipeline
                // before any other request code runs
                scriptHostManager.Initialize();
            }

            var webHostSettings = resolver.GetService<WebHostSettings>();
            if (webHostSettings.IsAuthDisabled)
            {
                request.SetProperty(ScriptConstants.AzureFunctionsHttpRequestAuthorizationDisabledKey, true);
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