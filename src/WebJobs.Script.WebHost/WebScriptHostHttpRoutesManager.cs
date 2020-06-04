// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal sealed partial class WebScriptHostHttpRoutesManager : IHttpRoutesManager
    {
        private readonly IOptions<HttpOptions> _httpOptions;
        private readonly IWebJobsRouter _router;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IEnvironment _environment;

        public WebScriptHostHttpRoutesManager(IOptions<HttpOptions> httpOptions, IWebJobsRouter router, ILoggerFactory loggerFactory, IEnvironment environment)
        {
            _httpOptions = httpOptions;
            _router = router;
            _loggerFactory = loggerFactory;
            _environment = environment;
        }

        public void InitializeHttpFunctionRoutes(IScriptJobHost host)
        {
            var routesLogBuilder = new StringBuilder();
            routesLogBuilder.AppendLine("Initializing function HTTP routes");

            _router.ClearRoutes();

            // TODO: FACAVAL Instantiation of the ScriptRouteHandler should be cleaned up
            WebJobsRouteBuilder routesBuilder = _router.CreateBuilder(new ScriptRouteHandler(_loggerFactory, host, _environment, false), _httpOptions.Value.RoutePrefix);

            // Proxies do not honor the route prefix defined in host.json
            WebJobsRouteBuilder proxiesRoutesBuilder = _router.CreateBuilder(new ScriptRouteHandler(_loggerFactory, host, _environment, true), routePrefix: null);

            foreach (var function in host.Functions)
            {
                var httpTrigger = function.HttpTriggerAttribute;
                if (httpTrigger != null)
                {
                    var constraints = new RouteValueDictionary();
                    if (httpTrigger.Methods != null)
                    {
                        constraints.Add("httpMethod", new HttpMethodRouteConstraint(httpTrigger.Methods));
                    }

                    string route = httpTrigger.Route;
                    bool isProxy = function.Metadata.IsProxy();

                    if (string.IsNullOrEmpty(route) && !isProxy)
                    {
                        route = function.Name;
                    }

                    WebJobsRouteBuilder builder = isProxy ? proxiesRoutesBuilder : routesBuilder;
                    builder.MapFunctionRoute(function.Metadata.Name, route, constraints, function.Metadata.Name);

                    LogRouteMap(routesLogBuilder, function.Metadata.Name, route, httpTrigger.Methods, isProxy, _httpOptions.Value.RoutePrefix);
                }
            }

            IRouter proxyRouter = null;
            IRouter functionRouter = null;

            if (routesBuilder.Count == 0 && proxiesRoutesBuilder.Count == 0)
            {
                routesLogBuilder.AppendLine("No HTTP routes mapped");
            }
            else
            {
                if (proxiesRoutesBuilder.Count > 0)
                {
                    proxyRouter = proxiesRoutesBuilder.Build();
                }

                if (routesBuilder.Count > 0)
                {
                    functionRouter = routesBuilder.Build();
                }
            }

            _router.AddFunctionRoutes(functionRouter, proxyRouter);

            ILogger logger = _loggerFactory.CreateLogger<WebScriptHostHttpRoutesManager>();
            logger.LogInformation(routesLogBuilder.ToString());
        }

        private void LogRouteMap(StringBuilder builder, string functionName, string route, string[] methods, bool isProxy, string prefix)
        {
            string methodList = methods is null ? "all" : string.Join(',', methods);

            if (isProxy)
            {
                builder.AppendLine($"Mapped proxy route '{route}' [{methodList}] to '{functionName}'");
            }
            else
            {
                builder.AppendLine($"Mapped function route '{prefix}/{route}' [{methodList}] to '{functionName}'");
            }
        }
    }
}
