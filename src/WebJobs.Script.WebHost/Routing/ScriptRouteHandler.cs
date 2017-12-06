// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Extensions.Http
{
    public class ScriptRouteHandler : IWebJobsRouteHandler
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly ILoggerFactory _loggerFactory;

        public ScriptRouteHandler(ILoggerFactory loggerFactory, WebScriptHostManager scriptHostManager)
        {
            _scriptHostManager = scriptHostManager;
            _loggerFactory = loggerFactory;
        }

        public async Task InvokeAsync(HttpContext context, string functionName)
        {
            // all function invocations require the host to be ready
            await _scriptHostManager.DelayUntilHostReady();

            // TODO: FACAVAL This should be improved....
            var host = _scriptHostManager.Instance;
            FunctionDescriptor descriptor = host.Functions.FirstOrDefault(f => string.Equals(f.Name, functionName));
            context.Features.Set<IFunctionExecutionFeature>(new FunctionExecutionFeature(host, descriptor));

            await Task.CompletedTask;
        }
    }
}