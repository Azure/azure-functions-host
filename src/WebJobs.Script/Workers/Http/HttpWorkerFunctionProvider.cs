// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    internal sealed class HttpWorkerFunctionProvider : IFunctionProvider
    {
        private readonly HttpWorkerOptions _httpWorkerOptions;
        private readonly IHostFunctionMetadataProvider _hostFunctionMetadataProvider;
        private readonly IOptionsMonitor<LanguageWorkerOptions> _languageWorkerOptions;
        private readonly ILogger _logger;
        private ImmutableDictionary<string, ImmutableArray<string>> _errors = ImmutableDictionary<string, ImmutableArray<string>>.Empty;
        private static readonly JArray AllHttpMethods = BuildAllHttpMethods();

        public HttpWorkerFunctionProvider(IOptions<HttpWorkerOptions> httpWorkerOptions, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions, IHostFunctionMetadataProvider hostFunctionMetadataProvider, ILogger<HttpWorkerFunctionProvider> logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(httpWorkerOptions?.Value);
            ArgumentNullException.ThrowIfNull(languageWorkerOptions);
            ArgumentNullException.ThrowIfNull(hostFunctionMetadataProvider);
            _httpWorkerOptions = httpWorkerOptions?.Value;
            _hostFunctionMetadataProvider = hostFunctionMetadataProvider;
            _languageWorkerOptions = languageWorkerOptions;
            _logger = logger;
        }

        public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors => _errors;

        private static JArray BuildAllHttpMethods()
        {
            return new JArray(
                HttpMethods.Get,
                HttpMethods.Post,
                HttpMethods.Put,
                HttpMethods.Delete,
                HttpMethods.Patch,
                HttpMethods.Head,
                HttpMethods.Options,
                HttpMethods.Trace,
                HttpMethods.Connect);
        }

        public async Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync()
        {
            var routes = _httpWorkerOptions.Http?.Routes;

            if (routes is null || !routes.Any())
            {
                return [];
            }

            if (!_httpWorkerOptions.CustomRoutesEnabled)
            {
                throw new InvalidOperationException($"Routes configuration is only allowed for worker runtime: custom");
            }

            var hostFunctionMetadata = await _hostFunctionMetadataProvider.GetFunctionMetadataAsync(_languageWorkerOptions.CurrentValue.WorkerConfigs, forceRefresh: false);

            // We already know custom handler http routes are configured, if function.json files are also present we cannot proceed.
            if (hostFunctionMetadata.Any())
            {
                throw new InvalidOperationException(
                    "Detected both function.json files and custom handler HTTP route configuration definition in host.json" +
                    "Only one configuration source is supported. Remove either the function.json files or the HTTP routes entries in host.json.");
            }

            return CreateFunctionsFromRoutes(routes);
        }

        private ImmutableArray<FunctionMetadata> CreateFunctionsFromRoutes(IEnumerable<HttpWorkerRoute> routes)
        {
            var metadataBuilder = ImmutableArray.CreateBuilder<FunctionMetadata>();
            var errorsBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>();

            int index = 0;

            foreach (var route in routes)
            {
                var functionName = $"http-handler{++index}";
                var routeTemplate = route.Route;

                if (string.IsNullOrWhiteSpace(routeTemplate))
                {
                    AddError("Route cannot be null, empty or whitespace.");
                    continue;
                }

                if (!TryParseRoute(routeTemplate, out var parseError))
                {
                    AddError(parseError!);
                    continue;
                }

                metadataBuilder.Add(CreateHttpFunctionMetadata(route, functionName));

                _logger.LogInformation(
                    "Created function {FunctionName} for route {RouteTemplate} (authLevel={AuthLevel}).",
                    functionName,
                    routeTemplate,
                    route.AuthorizationLevel);

                void AddError(string reason)
                {
                    errorsBuilder.Add(functionName, [reason]);
                    _logger.LogError(
                        "Unable to create function {FunctionName} for route {Route} due to invalid route: {Reason}",
                        functionName,
                        routeTemplate ?? "<null>",
                        reason);
                }
            }

            _errors = errorsBuilder.ToImmutable();

            return metadataBuilder.ToImmutable();

            bool TryParseRoute(string template, out string error)
            {
                try
                {
                    _ = TemplateParser.Parse(template);
                    error = null;
                    return true;
                }
                catch (ArgumentException ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        private static FunctionMetadata CreateHttpFunctionMetadata(HttpWorkerRoute route, string functionName)
        {
            var trigger = new JObject
            {
                ["type"] = "httpTrigger",
                ["authLevel"] = route.AuthorizationLevel.ToString(),
                ["direction"] = "in",
                ["name"] = "req",
                ["methods"] = AllHttpMethods,
                ["route"] = route.Route
            };

            var output = new JObject
            {
                ["type"] = "http",
                ["direction"] = "out",
                ["name"] = "res"
            };

            var metadata = new FunctionMetadata
            {
                Name = functionName
            };

            metadata.Bindings.Add(BindingMetadata.Create(trigger));
            metadata.Bindings.Add(BindingMetadata.Create(output));

            return metadata;
        }
    }
}
