// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ProxyMetadataManager : IProxyMetadataManager, IDisposable
    {
        private static readonly Regex ProxyNameValidationRegex = new Regex(@"[^a-zA-Z0-9_-]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly ReaderWriterLockSlim _metadataLock = new ReaderWriterLockSlim();
        private readonly IOptions<ScriptJobHostOptions> _scriptOptions;
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly IDisposable _fileChangeSubscription;
        private Lazy<ProxyMetadataInfo> _metadata;
        private bool _disposed = false;

        public ProxyMetadataManager(IOptions<ScriptJobHostOptions> scriptOptions, IEnvironment environment, IScriptEventManager eventManager, ILoggerFactory loggerFactory)
        {
            _scriptOptions = scriptOptions;
            _environment = environment;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _metadata = new Lazy<ProxyMetadataInfo>(LoadFunctionMetadata);

            _fileChangeSubscription = eventManager.OfType<FileEvent>()
                       .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal) &&
                       string.Equals(Path.GetFileName(f.FileChangeArguments.Name), ScriptConstants.ProxyMetadataFileName, StringComparison.OrdinalIgnoreCase))
                       .Subscribe(e => HandleProxyFileChange());
        }

        public ProxyMetadataInfo ProxyMetadata
        {
            get
            {
                _metadataLock.EnterReadLock();
                try
                {
                    return _metadata.Value;
                }
                finally
                {
                    _metadataLock.ExitReadLock();
                }
            }
        }

        private void HandleProxyFileChange()
        {
            _metadataLock.EnterWriteLock();
            try
            {
                if (_metadata.IsValueCreated)
                {
                    _metadata = new Lazy<ProxyMetadataInfo>(LoadFunctionMetadata);
                }
            }
            finally
            {
                _metadataLock.ExitWriteLock();
            }
        }

        private ProxyMetadataInfo LoadFunctionMetadata()
        {
            var functionErrors = new Dictionary<string, ICollection<string>>();
            (Collection<FunctionMetadata> proxies, ProxyClientExecutor client) = ReadProxyMetadata(_scriptOptions.Value.RootScriptPath, _logger, functionErrors);

            ImmutableArray<FunctionMetadata> metadata;
            if (proxies != null && proxies.Any())
            {
                metadata = proxies.ToImmutableArray();
            }

            var errors = functionErrors.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());
            return new ProxyMetadataInfo(metadata, errors, client);
        }

        internal static (Collection<FunctionMetadata>, ProxyClientExecutor) ReadProxyMetadata(string scriptPath, ILogger logger, Dictionary<string, ICollection<string>> functionErrors = null)
        {
            functionErrors = functionErrors ?? new Dictionary<string, ICollection<string>>();

            // read the proxy config
            string proxyConfigPath = Path.Combine(scriptPath, ScriptConstants.ProxyMetadataFileName);
            if (!File.Exists(proxyConfigPath))
            {
                return (null, null);
            }

            string proxiesJson = File.ReadAllText(proxyConfigPath);
            if (!string.IsNullOrWhiteSpace(proxiesJson))
            {
                logger.LogInformation("Loading proxies metadata");
                var values = LoadProxyMetadata(proxiesJson, functionErrors, logger);
                logger.LogInformation($"{values.Item1.Count} proxies loaded");
                return values;
            }

            return (null, null);
        }

        private static (Collection<FunctionMetadata>, ProxyClientExecutor) LoadProxyMetadata(string proxiesJson, Dictionary<string, ICollection<string>> functionErrors, ILogger logger)
        {
            var proxies = new Collection<FunctionMetadata>();
            ProxyClientExecutor client = null;

            var rawProxyClient = ProxyClientFactory.Create(proxiesJson, logger);
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

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _fileChangeSubscription?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
