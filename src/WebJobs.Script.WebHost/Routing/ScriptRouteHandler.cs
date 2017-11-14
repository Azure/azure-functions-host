// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Extensions.Http
{
    public class ScriptRouteHandler : IWebJobsRouteHandler
    {
        private readonly Func<ScriptHost> _scriptHostProvider;
        private readonly ILoggerFactory _loggerFactory;

        public ScriptRouteHandler(ILoggerFactory loggerFactory, Func<ScriptHost> scriptHostProvider)
        {
            _scriptHostProvider = scriptHostProvider;
            _loggerFactory = loggerFactory;
        }

        public ScriptRouteHandler(ILoggerFactory loggerFactory, ScriptHost scriptHost)
        {
            _loggerFactory = loggerFactory;
            _scriptHostProvider = () => scriptHost;
        }

        public async Task InvokeAsync(HttpContext context, string functionName)
        {
            // TODO: FACAVAL This should be improved....
            ScriptHost host = _scriptHostProvider();
            FunctionDescriptor descriptor = host.Functions.FirstOrDefault(f => string.Equals(f.Name, functionName));
            context.Features.Set<IFunctionExecutionFeature>(new FunctionExecutionFeature(host, descriptor));

            await Task.CompletedTask;
        }
    }
}