// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    internal class HttpWorkerFunctionProvider : IFunctionProvider
    {
        private const char SpaceChar = ' ';
        private const string DoubleSlash = "//";

        private readonly HttpWorkerOptions _httpWorkerOptions;
        private readonly IHostFunctionMetadataProvider _hostFunctionMetadataProvider;
        private readonly IOptionsMonitor<LanguageWorkerOptions> _languageWorkerOptions;
        private readonly ILogger _logger;
        private readonly Dictionary<string, List<string>> _errors = [];
        private static readonly ImmutableArray<string> HttpAllMethods = ["get", "post", "put", "delete", "patch", "head", "options"];

        public HttpWorkerFunctionProvider(IOptions<HttpWorkerOptions> httpWorkerOptions, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions, IHostFunctionMetadataProvider hostFunctionMetadataProvider, IEnvironment environment, ILogger<HttpWorkerFunctionProvider> logger)
        {
            _httpWorkerOptions = httpWorkerOptions?.Value ?? throw new ArgumentNullException(nameof(httpWorkerOptions));
            _hostFunctionMetadataProvider = hostFunctionMetadataProvider ?? throw new ArgumentNullException(nameof(hostFunctionMetadataProvider));
            _languageWorkerOptions = languageWorkerOptions ?? throw new ArgumentNullException(nameof(languageWorkerOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors =>
           _errors.ToImmutableDictionary(k => k.Key, v => v.Value.ToImmutableArray());

        public async Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync()
        {
            if (!string.Equals(_httpWorkerOptions.WorkerRuntime, ScriptConstants.CustomHandlerWorkerRuntime, StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            var routes = _httpWorkerOptions.HttpRoutes;
            if (routes is null || !routes.Any())
            {
                return [];
            }

            var hostFunctionMetadata = await _hostFunctionMetadataProvider.GetFunctionMetadataAsync(_languageWorkerOptions.CurrentValue.WorkerConfigs, forceRefresh: false);

            // We already know custom handler http routes are configured, if function.json files are also present we cannot proceed.
            if (hostFunctionMetadata.Any())
            {
                throw new InvalidOperationException(
                    "Detected both function.json files and custom handler HTTP route configuration definition in host.json" +
                    "Only one configuration source is supported. Remove either the function.json files or the HTTP routes entries in host.json.");
            }

            var routesLength = _httpWorkerOptions.HttpRoutes.Count();
            var functions = new Collection<FunctionMetadata>();

            for (int i = 0; i < routesLength; i++)
            {
                HttpWorkerRoute route = routes.ElementAt(i);
                var functionName = $"http-handler{i + 1}";

                if (!TryValidateHttpRoute(route?.Route, out string error))
                {
                    AddFunctionError(functionName, error);
                    _logger.LogError("Unable to create function '{functionName}' for '{route}' due to invalid route: {reason}", functionName, route, error);
                    continue;
                }

                functions.Add(CreateHttpFunctionMetadata(route, functionName));
                _logger.LogInformation("Created function '{functionName}' for route '{routeTemplate}' (authLevel={auth}).", functionName, route.Route, route.AuthorizationLevel);
            }

            return [.. functions];
        }

        private static bool TryValidateHttpRoute(string route, out string error)
        {
            error = null;

            // Basic constraints: no spaces, no double slashes (except root), balanced braces, no empty parameters.
            if (route.Contains(SpaceChar))
            {
                error = "Route template cannot contain spaces.";
                return false;
            }

            if (route.Contains(DoubleSlash))
            {
                error = "Route template cannot contain consecutive '/'.";
                return false;
            }

            // Balanced braces and no empty placeholders "{}"
            int depth = 0;
            for (int i = 0; i < route.Length; i++)
            {
                char c = route[i];
                if (c == '{')
                {
                    depth++;
                    // Empty param check: next must not be '}'
                    if (i + 1 < route.Length && route[i + 1] == '}')
                    {
                        error = "Route template contains an empty parameter '{}'.";
                        return false;
                    }
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth < 0)
                    {
                        error = "Route template contains unmatched closing brace '}'.";
                        return false;
                    }
                }
            }

            if (depth != 0)
            {
                error = "Route template contains unmatched '{'.";
                return false;
            }

            return true;
        }

        private void AddFunctionError(string functionName, string message)
        {
            if (!_errors.TryGetValue(functionName, out var list))
            {
                list = [];
                _errors[functionName] = list;
            }
            list.Add(message);
        }

        private static FunctionMetadata CreateHttpFunctionMetadata(HttpWorkerRoute route, string functionName)
        {
            var trigger = new BindingMetadata
            {
                Raw = new JObject
                {
                    ["type"] = "httpTrigger",
                    ["authLevel"] = route.AuthorizationLevel.ToString(),
                    ["direction"] = "in",
                    ["name"] = "req",
                    ["methods"] = new JArray(HttpAllMethods),
                    ["route"] = route.Route
                }
            };

            var output = new BindingMetadata
            {
                Raw = new JObject
                {
                    ["type"] = "http",
                    ["direction"] = "out",
                    ["name"] = "res"
                }
            };

            var metadata = new FunctionMetadata
            {
                Name = functionName
            };

            metadata.Bindings.Add(trigger);
            metadata.Bindings.Add(output);

            return metadata;
        }
    }
}
