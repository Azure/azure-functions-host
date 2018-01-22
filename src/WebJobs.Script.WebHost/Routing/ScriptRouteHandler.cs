// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Proxy;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Extensions.Http
{
    public class ScriptRouteHandler : IWebJobsRouteHandler
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly bool _isProxy;

        public ScriptRouteHandler(ILoggerFactory loggerFactory, WebScriptHostManager scriptHostManager, ScriptSettingsManager settingsManager, bool isProxy)
        {
            _scriptHostManager = scriptHostManager;
            _loggerFactory = loggerFactory;
            _settingsManager = settingsManager;
            _isProxy = isProxy;
        }

        public async Task InvokeAsync(HttpContext context, string functionName)
        {
            if (_isProxy)
            {
                ProxyFunctionExecutor proxyFunctionExecutor = new ProxyFunctionExecutor(_scriptHostManager);
                context.Items.TryAdd(ScriptConstants.AzureProxyFunctionExecutorKey, proxyFunctionExecutor);
            }

            // TODO: FACAVAL This should be improved....
            var host = _scriptHostManager.Instance;
            var descriptor = host.Functions.FirstOrDefault(f => string.Equals(f.Name, functionName));
            var executionFeature = new FunctionExecutionFeature(host, descriptor, _settingsManager, _loggerFactory);
            context.Features.Set<IFunctionExecutionFeature>(executionFeature);

            await Task.CompletedTask;
        }
    }
}