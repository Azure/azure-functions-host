// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ProxyMetadataManager : IProxyMetadataManager
    {
        private static readonly Regex ProxyNameValidationRegex = new Regex(@"[^a-zA-Z0-9_-]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly Lazy<ProxyMetadataInfo> _metadata;
        private readonly IOptions<ScriptJobHostOptions> _scriptOptions;
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;

        public ProxyMetadataManager(IOptions<ScriptJobHostOptions> scriptOptions, IEnvironment environment, ILoggerFactory loggerFactory)
        {
            _scriptOptions = scriptOptions;
            _environment = environment;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _metadata = new Lazy<ProxyMetadataInfo>(LoadFunctionMetadata);
        }

        public ProxyMetadataInfo ProxyMetadata => _metadata.Value;

        private ProxyMetadataInfo LoadFunctionMetadata()
        {
            var functionErrors = new Dictionary<string, ICollection<string>>();
            (Collection<FunctionMetadata> proxies, ProxyClientExecutor client) = ReadProxyMetadata(functionErrors);

            ImmutableArray<FunctionMetadata> metadata;
            if (proxies != null && proxies.Any())
            {
                metadata = proxies.ToImmutableArray();
            }

            var errors = functionErrors.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());
            return new ProxyMetadataInfo(metadata, errors, client);
        }

        internal (Collection<FunctionMetadata>, ProxyClientExecutor) ReadProxyMetadata(Dictionary<string, ICollection<string>> functionErrors)
        {
            // read the proxy config
            string proxyConfigPath = Path.Combine(_scriptOptions.Value.RootScriptPath, ScriptConstants.ProxyMetadataFileName);
            if (!File.Exists(proxyConfigPath))
            {
                return (null, null);
            }

            var proxyAppSettingValue = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ProxySiteExtensionEnabledKey);

            // This is for backward compatibility only, if the file is present but the value of proxy app setting(ROUTING_EXTENSION_VERSION) is explicitly set to 'disabled' we will ignore loading the proxies.
            if (!string.IsNullOrWhiteSpace(proxyAppSettingValue) && proxyAppSettingValue.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                return (null, null);
            }

            string proxiesJson = File.ReadAllText(proxyConfigPath);

            if (!string.IsNullOrWhiteSpace(proxiesJson))
            {
                return LoadProxyMetadata(proxiesJson, functionErrors);
            }

            return (null, null);
        }

        private (Collection<FunctionMetadata>, ProxyClientExecutor) LoadProxyMetadata(string proxiesJson, Dictionary<string, ICollection<string>> functionErrors)
        {
            var proxies = new Collection<FunctionMetadata>();
            ProxyClientExecutor client = null;

            var rawProxyClient = ProxyClientFactory.Create(proxiesJson, _logger);
            if (rawProxyClient != null)
            {
                client = new ProxyClientExecutor(rawProxyClient);
            }

            if (client == null)
            {
                return (proxies, null);
            }

            var routes = client.GetProxyData();

            foreach (var route in routes.Routes)
            {
                try
                {
                    // Proxy names should follow the same naming restrictions as in function names. If not, invalid characters will be removed.
                    var proxyName = NormalizeProxyName(route.Name);

                    var proxyMetadata = new FunctionMetadata();

                    var json = new JObject
                    {
                        { "authLevel", "anonymous" },
                        { "name", "req" },
                        { "type", "httptrigger" },
                        { "direction", "in" },
                        { "Route", route.UrlTemplate.TrimStart('/') },
                        { "Methods",  new JArray(route.Methods.Select(m => m.Method.ToString()).ToArray()) }
                    };

                    BindingMetadata bindingMetadata = BindingMetadata.Create(json);

                    proxyMetadata.Bindings.Add(bindingMetadata);

                    proxyMetadata.Name = proxyName;
                    proxyMetadata.IsProxy = true;

                    proxies.Add(proxyMetadata);
                }
                catch (Exception ex)
                {
                    // log any unhandled exceptions and continue
                    Utility.AddFunctionError(functionErrors, route.Name, Utility.FlattenException(ex, includeSource: false), isFunctionShortName: true);
                }
            }

            return (proxies, client);
        }

        internal static string NormalizeProxyName(string name)
        {
            return ProxyNameValidationRegex.Replace(name, string.Empty);
        }
    }
}
