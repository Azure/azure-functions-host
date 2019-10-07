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
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Logging;
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
        private readonly IFunctionMetadataProvider _functionMetadataProvider;
        private readonly HashSet<string> builtInBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "HttpTrigger", "Http", "TimerTrigger" };

        private static string[] _builtinExtensionAssemblies = GetBuiltinExtensionAssemblies();

        public ScriptStartupTypeLocator(string rootScriptPath, ILogger<ScriptStartupTypeLocator> logger, IExtensionBundleManager extensionBundleManager, IFunctionMetadataProvider functionMetadataProvider)
        {
            _rootScriptPath = rootScriptPath ?? throw new ArgumentNullException(nameof(rootScriptPath));
            _extensionBundleManager = extensionBundleManager ?? throw new ArgumentNullException(nameof(extensionBundleManager));
            _logger = logger;
            _functionMetadataProvider = functionMetadataProvider;
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
            IEnumerable<Type> startupTypes = GetExtensionsStartupTypesAsync().ConfigureAwait(false).GetAwaiter().GetResult();

            return startupTypes
                .Distinct(new TypeNameEqualityComparer())
                .ToArray();
        }

        private bool IsHttpOnly(HashSet<string> bindings)
        {
            var bindingCount = bindings.Count();
            // bindings other than http or timer are used
            if (bindingCount > 3)
            {
                return false;
            }
            else
            {
                bool hasOnlyHttpTriggerType = true;
                foreach (var binding in bindings)
                {
                    hasOnlyHttpTriggerType = hasOnlyHttpTriggerType && builtInBindings.Contains(binding);
                }
                return hasOnlyHttpTriggerType;
            }
        }

        public async Task<IEnumerable<Type>> GetExtensionsStartupTypesAsync()
        {
            var bundleConfigured = _extensionBundleManager.IsExtensionBundleConfigured();
            var functionMetadataCollection = _functionMetadataProvider.GetFunctionMetadata(forceRefresh: true);
            var startupTypes = new List<Type>();
            var bindingsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (bundleConfigured)
            {
                // Generate a Hashset of all the binding types used in the function app
                foreach (var functionMetadata in functionMetadataCollection)
                {
                    foreach (var binding in functionMetadata.Bindings)
                    {
                        bindingsSet.Add(binding.Type);
                    }
                }

                // If extension bundle is configured and the function app is only using http or timer,
                // then no need to go find bundle or parse through any metadata from bindings.json.
                // This is would save us a bunch of IO operations
                if (IsHttpOnly(bindingsSet))
                {
                    return startupTypes;
                }
            }

            string binPath;
            if (bundleConfigured)
            {
                string extensionBundlePath = await _extensionBundleManager.GetExtensionBundlePath();
                if (string.IsNullOrEmpty(extensionBundlePath))
                {
                    _logger.ScriptStartUpErrorLoadingExtensionBundle();
                    return startupTypes;
                }
                _logger.ScriptStartUpLoadingExtensionBundle(extensionBundlePath);
                binPath = Path.Combine(extensionBundlePath, "bin");
            }
            else
            {
                binPath = Path.Combine(_rootScriptPath, "bin");
            }



            string metadataFilePath = Path.Combine(binPath, ScriptConstants.ExtensionsMetadataFileName);
            // parse the extensions file to get declared startup extensions
            ExtensionReference[] extensionItems = ParseExtensions(metadataFilePath);

            foreach (var extensionItem in extensionItems)
            {
                if (!bundleConfigured
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
                                path = Path.Combine(binPath, path);
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
                    if (!typeof(IWebJobsStartup).IsAssignableFrom(extensionType))
                    {
                        _logger.ScriptStartUpTypeIsNotValid(extensionItem.TypeName, nameof(IWebJobsStartup));
                        continue;
                    }
                    startupTypes.Add(extensionType);
                }
            }

            return startupTypes;
        }

        private ExtensionReference[] ParseExtensions(string metadataFilePath)
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
