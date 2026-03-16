// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
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

            WebJobsRouteBuilder warmupRouteBuilder = null;
            if (!_environment.IsAnyLinuxConsumption() && !_environment.IsWindowsConsumption())
            {
                warmupRouteBuilder = _router.CreateBuilder(new ScriptRouteHandler(_loggerFactory, host, _environment, isProxy: false, isWarmup: true), routePrefix: "admin");
            }

            foreach (var function in host.Functions)
            {
                var httpTrigger = function.HttpTriggerAttribute;
                if (httpTrigger != null)
                {
                    var constraintMethods = BuildConstraintMethods(httpTrigger.Methods);
                    var constraints = new RouteValueDictionary();
                    if (constraintMethods is not null)
                    {
                        constraints.Add("httpMethod", new HttpMethodRouteConstraint(constraintMethods));
                    }

                    string route = httpTrigger.Route;
                    bool isProxy = function.Metadata.IsProxy();

                    if (string.IsNullOrEmpty(route) && !isProxy)
                    {
                        route = function.Name;
                    }

                    WebJobsRouteBuilder builder = isProxy ? proxiesRoutesBuilder : routesBuilder;
                    builder.MapFunctionRoute(function.Metadata.Name, route, constraints, function.Metadata.Name);

                    // Register HEAD-only shadow route for 405 responses.
                    if (!isProxy && ShouldRegisterHeadNotAllowedRoute(httpTrigger.Methods))
                    {
                        var headConstraints = new RouteValueDictionary();
                        headConstraints.Add("httpMethod", new HttpMethodRouteConstraint("head"));
                        string sentinelName = BuildHeadNotAllowedSentinelName(httpTrigger.Methods);
                        builder.MapFunctionRoute(sentinelName, route, headConstraints, sentinelName);
                    }

                    LogRouteMap(routesLogBuilder, function.Metadata.Name, route, httpTrigger.Methods, isProxy, _httpOptions.Value.RoutePrefix);
                }
                else if (warmupRouteBuilder != null && !_environment.IsInValidationMode() && function.IsWarmupFunction())
                {
                    warmupRouteBuilder.MapFunctionRoute(function.Metadata.Name, "warmup", function.Metadata.Name);
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

        // Returns the methods array to use for the route constraint.
        // Adds "head" when "get" is present and "head" is not.
        internal static string[] BuildConstraintMethods(string[] triggerMethods)
        {
            if (triggerMethods is null)
            {
                return null;
            }

            bool hasGet = triggerMethods.Contains("get", StringComparer.OrdinalIgnoreCase);
            bool hasHead = triggerMethods.Contains("head", StringComparer.OrdinalIgnoreCase);

            if (hasGet && !hasHead)
            {
                return [.. triggerMethods, "head"];
            }

            return triggerMethods;
        }

        // Returns true when a HEAD shadow-route should be registered for 405 handling.
        // True only when the function accepts neither GET nor HEAD.
        internal static bool ShouldRegisterHeadNotAllowedRoute(string[] triggerMethods)
        {
            if (triggerMethods is null)
            {
                return false;
            }

            bool hasGet = triggerMethods.Contains("get", StringComparer.OrdinalIgnoreCase);
            bool hasHead = triggerMethods.Contains("head", StringComparer.OrdinalIgnoreCase);

            return !hasGet && !hasHead;
        }

        // Builds the sentinel route name for a HEAD-405 shadow route.
        // Format: "$head_not_allowed:POST, PUT" (methods uppercased, joined with ", ").
        internal static string BuildHeadNotAllowedSentinelName(string[] triggerMethods)
            => ScriptConstants.HeadMethodNotAllowedPrefix
               + string.Join(", ", triggerMethods.Select(m => m.ToUpperInvariant()));
    }
}
