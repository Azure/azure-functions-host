// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    /// <summary>
    /// An implementation of an <see cref="IWebJobsStartupTypeLocator"/> that locates startup types
    /// from extension registrations.
    /// </summary>
    public class ScriptStartupTypeLocator : IWebJobsStartupTypeLocator
    {
        private readonly string _rootScriptPath;
        private readonly ILogger _logger;
        private readonly IExtensionBundleManager _extensionBundleManager;
        private readonly IFunctionMetadataManager _functionMetadataManager;
        private readonly IMetricsLogger _metricsLogger;
        private readonly Lazy<IEnumerable<Type>> _startupTypes;
        private readonly IOptions<LanguageWorkerOptions> _languageWorkerOptions;
        private static string[] _builtinExtensionAssemblies = GetBuiltinExtensionAssemblies();

        public ScriptStartupTypeLocator(string rootScriptPath, ILogger<ScriptStartupTypeLocator> logger, IExtensionBundleManager extensionBundleManager,
            IFunctionMetadataManager functionMetadataManager, IMetricsLogger metricsLogger, IOptions<LanguageWorkerOptions> languageWorkerOptions)
        {
            _rootScriptPath = rootScriptPath ?? throw new ArgumentNullException(nameof(rootScriptPath));
            _extensionBundleManager = extensionBundleManager ?? throw new ArgumentNullException(nameof(extensionBundleManager));
            _logger = logger;
            _functionMetadataManager = functionMetadataManager;
            _metricsLogger = metricsLogger;
            _startupTypes = new Lazy<IEnumerable<Type>>(() => GetExtensionsStartupTypesAsync().ConfigureAwait(false).GetAwaiter().GetResult());
            _languageWorkerOptions = languageWorkerOptions;
        }

        private static string[] GetBuiltinExtensionAssemblies()
        {
            return new[]
            {
                typeof(WebJobs.Extensions.Http.HttpWebJobsStartup).Assembly.GetName().Name,
                typeof(WebJobs.Extensions.ExtensionsWebJobsStartup).Assembly.GetName().Name
            };
        }

        public Type[] GetStartupTypes()
        {
            return _startupTypes.Value
                .Distinct(new TypeNameEqualityComparer())
                .ToArray();
        }

        internal bool HasExternalConfigurationStartups() => _startupTypes.Value.Any(p => typeof(IWebJobsConfigurationStartup).IsAssignableFrom(p));

        public async Task<IEnumerable<Type>> GetExtensionsStartupTypesAsync()
        {
            string extensionsPath;
            FunctionAssemblyLoadContext.ResetSharedContext();

            HashSet<string> bindingsSet = null;
            var bundleConfigured = _extensionBundleManager.IsExtensionBundleConfigured();
            bool isLegacyExtensionBundle = _extensionBundleManager.IsLegacyExtensionBundle();
            bool isPrecompiledFunctionApp = false;

            // if workerIndexing
            //      Function.json (httpTrigger, blobTrigger, blobTrigger)  -> httpTrigger, blobTrigger
            // dotnet app precompiled -> Do not use bundles
            var workerConfigs = _languageWorkerOptions.Value.WorkerConfigs;
            if (bundleConfigured && !Utility.CanWorkerIndex(workerConfigs, SystemEnvironment.Instance))
            {
                var functionMetadataCollection = _functionMetadataManager.GetFunctionMetadata(forceRefresh: true, includeCustomProviders: false);
                bindingsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Generate a Hashset of all the binding types used in the function app
                foreach (var functionMetadata in functionMetadataCollection)
                {
                    foreach (var binding in functionMetadata.Bindings)
                    {
                        bindingsSet.Add(binding.Type);
                    }
                    isPrecompiledFunctionApp = isPrecompiledFunctionApp || functionMetadata.Language == DotNetScriptTypes.DotNetAssembly;
                }
            }

            if (SystemEnvironment.Instance.IsPlaceholderModeEnabled())
            {
                // Do not move this.
                // Calling this log statement in the placeholder mode to avoid jitting during specializtion
                _logger.ScriptStartNotLoadingExtensionBundle("WARMUP_LOG_ONLY", bundleConfigured, isPrecompiledFunctionApp, isLegacyExtensionBundle);
            }

            if (bundleConfigured && (!isPrecompiledFunctionApp || isLegacyExtensionBundle))
            {
                extensionsPath = await _extensionBundleManager.GetExtensionBundleBinPathAsync();
                if (string.IsNullOrEmpty(extensionsPath))
                {
                    _logger.ScriptStartUpErrorLoadingExtensionBundle();
                    return new Type[0];
                }
                _logger.ScriptStartUpLoadingExtensionBundle(extensionsPath);
            }
            else
            {
                extensionsPath = Path.Combine(_rootScriptPath, "bin");

                if (!File.Exists(Path.Combine(extensionsPath, ScriptConstants.ExtensionsMetadataFileName)) &&
                    File.Exists(Path.Combine(_rootScriptPath, ScriptConstants.ExtensionsMetadataFileName)))
                {
                    // As a fallback, allow extensions.json in the root path.
                    extensionsPath = _rootScriptPath;
                }

                _logger.ScriptStartNotLoadingExtensionBundle(extensionsPath, bundleConfigured, isPrecompiledFunctionApp, isLegacyExtensionBundle);
            }

            string metadataFilePath = Path.Combine(extensionsPath, ScriptConstants.ExtensionsMetadataFileName);

            // parse the extensions file to get declared startup extensions
            ExtensionReference[] extensionItems = ParseExtensions(metadataFilePath);

            var startupTypes = new List<Type>();

            foreach (var extensionItem in extensionItems)
            {
                if (Utility.CanWorkerIndex(workerConfigs, SystemEnvironment.Instance)
                    || !bundleConfigured
                    || extensionItem.Bindings.Count == 0
                    || extensionItem.Bindings.Intersect(bindingsSet, StringComparer.OrdinalIgnoreCase).Any())
                {
                    string startupExtensionName = extensionItem.Name ?? extensionItem.TypeName;
                    _logger.ScriptStartUpLoadingStartUpExtension(startupExtensionName);

                    // load the Type for each startup extension into the function assembly load context
                    Type extensionType = Type.GetType(extensionItem.TypeName,
                        assemblyName =>
                        {
                            if (_builtinExtensionAssemblies.Contains(assemblyName.Name, StringComparer.OrdinalIgnoreCase))
                            {
                                _logger.ScriptStartUpBelongExtension(extensionItem.TypeName);
                                return null;
                            }

                            string path = extensionItem.HintPath;
                            if (string.IsNullOrEmpty(path))
                            {
                                path = assemblyName.Name + ".dll";
                            }

                            var hintUri = new Uri(path, UriKind.RelativeOrAbsolute);
                            if (!hintUri.IsAbsoluteUri)
                            {
                                path = Path.Combine(extensionsPath, path);
                            }

                            if (File.Exists(path))
                            {
                                return FunctionAssemblyLoadContext.Shared.LoadFromAssemblyPath(path, true);
                            }

                            return null;
                        },
                        (assembly, typeName, ignoreCase) =>
                        {
                            _logger.ScriptStartUpLoadedExtension(startupExtensionName, assembly.GetName().Version.ToString());
                            return assembly?.GetType(typeName, false, ignoreCase);
                        }, false, true);

                    if (extensionType == null)
                    {
                        _logger.ScriptStartUpUnableToLoadExtension(startupExtensionName, extensionItem.TypeName);
                        continue;
                    }

                    if (!typeof(IWebJobsStartup).IsAssignableFrom(extensionType) && !typeof(IWebJobsConfigurationStartup).IsAssignableFrom(extensionType))
                    {
                        _logger.ScriptStartUpTypeIsNotValid(extensionItem.TypeName, nameof(IWebJobsStartup), nameof(IWebJobsConfigurationStartup));
                        continue;
                    }

                    startupTypes.Add(extensionType);
                }
            }

            return startupTypes;
        }

        private ExtensionReference[] ParseExtensions(string metadataFilePath)
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.ParseExtensions))
            {
                if (!File.Exists(metadataFilePath))
                {
                    return Array.Empty<ExtensionReference>();
                }

                try
                {
                    var extensionMetadata = JObject.Parse(File.ReadAllText(metadataFilePath));

                    var extensionItems = extensionMetadata["extensions"]?.ToObject<List<ExtensionReference>>();
                    if (extensionItems == null)
                    {
                        _logger.ScriptStartUpUnableParseMetadataMissingProperty(metadataFilePath);
                        return Array.Empty<ExtensionReference>();
                    }

                    return extensionItems.ToArray();
                }
                catch (JsonReaderException exc)
                {
                    _logger.ScriptStartUpUnableParseMetadata(exc, metadataFilePath);

                    return Array.Empty<ExtensionReference>();
                }
            }
        }

        private class TypeNameEqualityComparer : IEqualityComparer<Type>
        {
            public bool Equals(Type x, Type y)
            {
                if (x == y)
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return string.Equals(x.FullName, y.FullName, StringComparison.Ordinal);
            }

            public int GetHashCode(Type obj)
            {
                return obj?.FullName?.GetHashCode() ?? 0;
            }
        }
    }
}
