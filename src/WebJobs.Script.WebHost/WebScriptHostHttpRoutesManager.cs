// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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

            WebJobsRouteBuilder warmupRouteBuilder = null;
            if (!_environment.IsLinuxConsumption() && !_environment.IsWindowsConsumption())
            {
                warmupRouteBuilder = _router.CreateBuilder(new ScriptRouteHandler(_loggerFactory, host, _environment, isWarmup: true), routePrefix: "admin");
            }

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

                    if (string.IsNullOrEmpty(route))
                    {
                        route = function.Name;
                    }

                    routesBuilder.MapFunctionRoute(function.Metadata.Name, route, constraints, function.Metadata.Name);

                    LogRouteMap(routesLogBuilder, function.Metadata.Name, route, httpTrigger.Methods, _httpOptions.Value.RoutePrefix);
                }
                else if (warmupRouteBuilder != null && function.IsWarmupFunction())
                {
                    warmupRouteBuilder.MapFunctionRoute(function.Metadata.Name, "warmup", function.Metadata.Name);
                }
            }

            IRouter proxyRouter = null;
            IRouter functionRouter = null;
            if (routesBuilder.Count == 0)
            {
                routesLogBuilder.AppendLine("No HTTP routes mapped");
            }
            else
            {
                functionRouter = routesBuilder.Build();
            }

            _router.AddFunctionRoutes(functionRouter, proxyRouter);

            if (warmupRouteBuilder != null)
            {
                // Adding the default admin/warmup route when no warmup function is present
                if (warmupRouteBuilder.Count == 0)
                {
                    warmupRouteBuilder.MapFunctionRoute(string.Empty, "warmup", string.Empty);
                }
                IRouter warmupRouter = warmupRouteBuilder.Build();
                _router.AddFunctionRoutes(warmupRouter, null);
            }

            ILogger logger = _loggerFactory.CreateLogger<WebScriptHostHttpRoutesManager>();
            logger.LogInformation(routesLogBuilder.ToString());
        }

        private void LogRouteMap(StringBuilder builder, string functionName, string route, string[] methods, string prefix)
        {
            string methodList = methods is null ? "all" : string.Join(',', methods);

            builder.AppendLine($"Mapped function route '{prefix}/{route}' [{methodList}] to '{functionName}'");
        }
    }
}
