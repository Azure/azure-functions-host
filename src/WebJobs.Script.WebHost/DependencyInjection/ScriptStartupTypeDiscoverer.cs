// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    /// <summary>
    /// An implementation of an <see cref="IWebJobsStartupTypeDiscoverer"/> that locates startup types
    /// from extension registrations and function extension references
    /// </summary>
    public class ScriptStartupTypeDiscoverer : IWebJobsStartupTypeDiscoverer
    {
        private readonly string _rootScriptPath;
        // TODO: DI (FACAVAL) Pass a logger. This will be the root application logger.
        // need to forward this to the user logs once created
        private readonly ILogger _logger = NullLogger.Instance;

        public ScriptStartupTypeDiscoverer(string rootScriptPath)
        {
            _rootScriptPath = rootScriptPath ?? throw new ArgumentNullException(nameof(rootScriptPath));
        }

        public Type[] GetStartupTypes()
        {
            IEnumerable<Type> startupTypes = GetExtensionsStartupTypes();

            return startupTypes
                .Distinct(new TypeNameEqualityComparer())
                .ToArray();
        }

        public IEnumerable<Type> GetExtensionsStartupTypes()
        {
            string binPath = Path.Combine(_rootScriptPath, "bin");

            string metadataFilePath = Path.Combine(binPath, ScriptConstants.ExtensionsMetadataFileName);
            ExtensionReference[] extensionItems = ParseExtensions(metadataFilePath);

            var startupTypes = new List<Type>();

            foreach (var item in extensionItems)
            {
                string extensionName = item.Name ?? item.TypeName;

                _logger.LogInformation($"Loading custom extension '{extensionName}'");

                Type extensionType = Type.GetType(item.TypeName,
                    assemblyName =>
                    {
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
                    _logger.LogWarning($"Unable to load custom extension '{extensionName}' (Type: `{item.TypeName}`)." +
                            $"The type does not exist. Please validate the type and assembly names.");
                    continue;
                }

                IEnumerable<Type> assemblyStartupTypes = TryResolveStartupType(extensionType);

                if (!assemblyStartupTypes.Any())
                {
                    _logger.LogWarning($"Unable to load custom extension '{extensionName}' (Type: `{item.TypeName}`)." +
                            $"No WebJobsStartupAttribute applied to the assembly '{extensionType.Assembly}'.");
                }
                else
                {
                    startupTypes.AddRange(assemblyStartupTypes);
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
                    _logger.LogError($"Unable to parse extensions metadata file '{metadataFilePath}'. Missing 'extensions' property.");
                    return Array.Empty<ExtensionReference>();
                }

                return extensionItems.ToArray();
            }
            catch (JsonReaderException exc)
            {
                _logger.LogError(exc, $"Unable to parse extensions metadata file '{metadataFilePath}'");

                return Array.Empty<ExtensionReference>();
            }
        }

        private IEnumerable<Type> TryResolveStartupType(Type extensionType)
        {
            // If explicitly referencing a type that implements IWebJobsStartup, return the type
            if (typeof(IWebJobsStartup).IsAssignableFrom(extensionType))
            {
                return new[] { extensionType };
            }

            return GetAssemblyStartupType(extensionType.Assembly);
        }

        private IEnumerable<Type> GetAssemblyStartupType(Assembly assembly)
        {
            IEnumerable<WebJobsStartupAttribute> startupAttributes = assembly.GetCustomAttributes<WebJobsStartupAttribute>();

            return startupAttributes.Select(a => a.WebJobsStartupType);
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
                if (obj == null)
                {
                    return 0;
                }

                return obj.FullName.GetHashCode();
            }
        }
    }
}
