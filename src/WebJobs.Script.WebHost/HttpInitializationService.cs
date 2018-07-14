// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// An <see cref="IHostedService"/> responsible for HTTP function initialization.
    /// </summary>
    public class HttpInitializationService : IHostedService
    {
        private readonly IOptions<HttpExtensionOptions> _httpOptions;
        private readonly IWebJobsRouter _router;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IScriptJobHost _host;

        public HttpInitializationService(IOptions<HttpExtensionOptions> httpOptions, IWebJobsRouter router, ILoggerFactory loggerFactory, IScriptJobHost host)
        {
            _httpOptions = httpOptions;
            _router = router;
            _loggerFactory = loggerFactory;
            _host = host;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            InitializeHttpFunctions(_host.Functions, _httpOptions.Value);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void InitializeHttpFunctions(IEnumerable<FunctionDescriptor> functions, HttpExtensionOptions httpOptions)
        {
            _router.ClearRoutes();

            // TODO: FACAVAL Instantiation of the ScriptRouteHandler should be cleaned up
            // TODO: DI (FACAVAL) Remove settings manager usage
            WebJobsRouteBuilder routesBuilder = _router.CreateBuilder(new ScriptRouteHandler(_loggerFactory, _host, ScriptSettingsManager.Instance, false), httpOptions.RoutePrefix);

            // Proxies do not honor the route prefix defined in host.json
            // TODO: DI (FACAVAL) Remove settings manager usage
            WebJobsRouteBuilder proxiesRoutesBuilder = _router.CreateBuilder(new ScriptRouteHandler(_loggerFactory, _host, ScriptSettingsManager.Instance, true), routePrefix: null);

            foreach (var function in functions)
            {
                var httpTrigger = function.GetTriggerAttributeOrNull<HttpTriggerAttribute>();
                if (httpTrigger != null)
                {
                    var constraints = new RouteValueDictionary();
                    if (httpTrigger.Methods != null)
                    {
                        constraints.Add("httpMethod", new HttpMethodRouteConstraint(httpTrigger.Methods));
                    }

                    string route = httpTrigger.Route;

                    if (string.IsNullOrEmpty(route) && !function.Metadata.IsProxy)
                    {
                        route = function.Name;
                    }

                    WebJobsRouteBuilder builder = function.Metadata.IsProxy ? proxiesRoutesBuilder : routesBuilder;
                    builder.MapFunctionRoute(function.Metadata.Name, route, constraints, function.Metadata.Name);
                }
            }

            IRouter proxyRouter = null;
            IRouter functionRouter = null;

            if (proxiesRoutesBuilder.Count > 0)
            {
                proxyRouter = proxiesRoutesBuilder.Build();
            }

            if (routesBuilder.Count > 0)
            {
                functionRouter = routesBuilder.Build();
            }

            _router.AddFunctionRoutes(functionRouter, proxyRouter);
        }
    }
}
