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
using System.Threading.Tasks;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ProxyFunctionProvider : IFunctionProvider, IDisposable
    {
        private static readonly Regex ProxyNameValidationRegex = new Regex(@"[^a-zA-Z0-9_-]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly ReaderWriterLockSlim _metadataLock = new ReaderWriterLockSlim();
        private readonly IOptions<ScriptJobHostOptions> _scriptOptions;
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly IDisposable _fileChangeSubscription;
        private Dictionary<string, ICollection<string>> _functionErrors = new Dictionary<string, ICollection<string>>();
        private Lazy<ImmutableArray<FunctionMetadata>> _metadata;
        private bool _disposed = false;

        public ProxyFunctionProvider(IOptions<ScriptJobHostOptions> scriptOptions, IEnvironment environment, IScriptEventManager eventManager, ILoggerFactory loggerFactory)
        {
            _scriptOptions = scriptOptions;
            _environment = environment;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _metadata = new Lazy<ImmutableArray<FunctionMetadata>>(LoadFunctionMetadata);

            _fileChangeSubscription = eventManager.OfType<FileEvent>()
                       .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal) &&
                       string.Equals(Path.GetFileName(f.FileChangeArguments.Name), ScriptConstants.ProxyMetadataFileName, StringComparison.OrdinalIgnoreCase))
                       .Subscribe(e => HandleProxyFileChange());
        }

        public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors => _functionErrors.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());

        public Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync()
        {
            _metadataLock.EnterReadLock();
            try
            {
                return Task.FromResult(_metadata.Value);
            }
            finally
            {
                _metadataLock.ExitReadLock();
            }
        }

        private void HandleProxyFileChange()
        {
            _metadataLock.EnterWriteLock();
            try
            {
                if (_metadata.IsValueCreated)
                {
                    _metadata = new Lazy<ImmutableArray<FunctionMetadata>>(LoadFunctionMetadata);
                }
            }
            finally
            {
                _metadataLock.ExitWriteLock();
            }
        }

        private ImmutableArray<FunctionMetadata> LoadFunctionMetadata()
        {
            var functionErrors = new Dictionary<string, ICollection<string>>();
            Collection<ProxyFunctionMetadata> proxies = ReadProxyMetadata(_scriptOptions.Value.RootScriptPath, _logger, functionErrors);

            ImmutableArray<FunctionMetadata> metadata;
            if (proxies != null && proxies.Any())
            {
                metadata = proxies.ToImmutableArray<FunctionMetadata>();
            }

            _functionErrors = functionErrors;
            return metadata;
        }

        internal static Collection<ProxyFunctionMetadata> ReadProxyMetadata(string scriptPath, ILogger logger, Dictionary<string, ICollection<string>> functionErrors = null)
        {
            functionErrors = functionErrors ?? new Dictionary<string, ICollection<string>>();

            // read the proxy config
            string proxyConfigPath = Path.Combine(scriptPath, ScriptConstants.ProxyMetadataFileName);
            if (!File.Exists(proxyConfigPath))
            {
                return null;
            }

            string proxiesJson = File.ReadAllText(proxyConfigPath);
            if (!string.IsNullOrWhiteSpace(proxiesJson))
            {
                logger.LogInformation("Loading proxies metadata");
                var metadataCollection = LoadProxyMetadata(proxiesJson, functionErrors, logger);
                logger.LogInformation($"{metadataCollection.Count} proxies loaded");
                return metadataCollection;
            }

            return null;
        }

        private static Collection<ProxyFunctionMetadata> LoadProxyMetadata(string proxiesJson, Dictionary<string, ICollection<string>> functionErrors, ILogger logger)
        {
            var proxies = new Collection<ProxyFunctionMetadata>();
            ProxyClientExecutor client = null;

            var rawProxyClient = ProxyClientFactory.Create(proxiesJson, logger);
            if (rawProxyClient != null)
            {
                client = new ProxyClientExecutor(rawProxyClient);
            }

            if (client == null)
            {
                return proxies;
            }

            var routes = client.GetProxyData();

            foreach (var route in routes.Routes)
            {
                try
                {
                    // Proxy names should follow the same naming restrictions as in function names. If not, invalid characters will be removed.
                    var proxyName = NormalizeProxyName(route.Name);

                    var proxyMetadata = new ProxyFunctionMetadata(client);

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
                    proxies.Add(proxyMetadata);
                }
                catch (Exception ex)
                {
                    // log any unhandled exceptions and continue
                    Utility.AddFunctionError(functionErrors, route.Name, Utility.FlattenException(ex, includeSource: false), isFunctionShortName: true);
                }
            }

            return proxies;
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
