// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

        private static string[] _builtinExtensionAssemblies = GetBuiltinExtensionAssemblies();

        public ScriptStartupTypeLocator(string rootScriptPath, IExtensionBundleManager extensionBundleManager)
            : this(rootScriptPath, NullLogger.Instance, extensionBundleManager)
        {
        }

        public ScriptStartupTypeLocator(string rootScriptPath, ILogger logger, IExtensionBundleManager extensionBundleManager)
        {
            _rootScriptPath = rootScriptPath ?? throw new ArgumentNullException(nameof(rootScriptPath));
            _extensionBundleManager = extensionBundleManager ?? throw new ArgumentNullException(nameof(extensionBundleManager));
            _logger = logger;
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

        public async Task<IEnumerable<Type>> GetExtensionsStartupTypesAsync()
        {
            string binPath;
            if (_extensionBundleManager.IsExtensionBundleConfigured())
            {
                string extensionBundlePath = await _extensionBundleManager.GetExtensionBundlePath();
                if (string.IsNullOrEmpty(extensionBundlePath))
                {
                    _logger.ScriptStartUpErrorLoadingExtensionBundle();
                    return null;
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

            var startupTypes = new List<Type>();

            foreach (var item in extensionItems)
            {
                string startupExtensionName = item.Name ?? item.TypeName;
                _logger.ScriptStartUpLoadingStartUpExtension(startupExtensionName);

                // load the Type for each startup extension into the function assembly load context
                Type extensionType = Type.GetType(item.TypeName,
                    assemblyName =>
                    {
                        if (_builtinExtensionAssemblies.Contains(assemblyName.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            _logger.ScriptStartUpBelongExtension(item.TypeName);
                            return null;
                        }

                        string path = item.HintPath;
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
                        return assembly?.GetType(typeName, false, ignoreCase);
                    }, false, true);

                if (extensionType == null)
                {
                    _logger.ScriptStartUpUnableToLoadExtension(startupExtensionName, item.TypeName);
                    continue;
                }
                if (!typeof(IWebJobsStartup).IsAssignableFrom(extensionType))
                {
                    _logger.ScriptStartUpTypeIsNotValid(item.TypeName, nameof(IWebJobsStartup));
                    continue;
                }

                startupTypes.Add(extensionType);
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
