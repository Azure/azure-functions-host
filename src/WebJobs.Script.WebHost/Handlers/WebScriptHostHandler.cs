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
            _config = config ?? throw new ArgumentNullException("config");
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resolver = _config.DependencyResolver;

            // Need to ensure the host manager is initialized early in the pipeline
            // before any other request code runs.
            var scriptHostManager = resolver.GetService<WebScriptHostManager>();
            scriptHostManager.EnsureInitialized();
            request.Properties[ScriptConstants.TracePropertyInstanceIdKey] = scriptHostManager.Instance?.InstanceId ?? string.Empty;

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
    }
}