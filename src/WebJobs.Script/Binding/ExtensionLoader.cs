// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class ExtensionLoader
    {
        private readonly ScriptHostConfiguration _config;
        private readonly ILogger _logger;

        public ExtensionLoader(ScriptHostConfiguration config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public IEnumerable<string> DiscoverBindingTypes(IEnumerable<FunctionMetadata> functions)
        {
            HashSet<string> bindingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var function in functions)
            {
                foreach (var binding in function.InputBindings.Concat(function.OutputBindings))
                {
                    string bindingType = binding.Type;
                    bindingTypes.Add(bindingType);
                }
            }
            return bindingTypes;
        }

        // Load extensions that are directly references by the user types.
        public void LoadDirectlyReferencedExtensions(IEnumerable<Type> userTypes)
        {
            var possibleExtensionAssemblies = UserTypeScanner.GetPossibleExtensionAssemblies(userTypes);

            foreach (var kv in possibleExtensionAssemblies)
            {
                var assembly = kv.Key;
                var locationHint = kv.Value;
                LoadExtensions(assembly, locationHint);
            }
        }

        public void LoadCustomExtensions()
        {
            string binPath = Path.Combine(_config.RootScriptPath, "bin");
            string metadataFilePath = Path.Combine(binPath, ScriptConstants.ExtensionsMetadataFileName);
            if (File.Exists(metadataFilePath))
            {
                var extensionMetadata = JObject.Parse(File.ReadAllText(metadataFilePath));

                var extensionItems = extensionMetadata["extensions"]?.ToObject<List<ExtensionReference>>();
                if (extensionItems == null)
                {
                    _logger.LogWarning("Invalid extensions metadata file. Unable to load custom extensions");
                    return;
                }

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
                                return Assembly.LoadFrom(path);
                            }

                            return null;
                        },
                        (assembly, typeName, ignoreCase) =>
                        {
                            return assembly?.GetType(typeName, false, ignoreCase);
                        }, false, true);

                    if (extensionType == null ||
                        !LoadIfExtensionType(extensionType, extensionType.Assembly.Location))
                    {
                        _logger.LogWarning($"Unable to load custom extension type for extension '{extensionName}' (Type: `{item.TypeName}`)." +
                                $"The type does not exist or is not a valid extension. Please validate the type and assembly names.");
                    }
                }
            }
        }

        private void LoadExtensions(Assembly assembly, string locationHint)
        {
            foreach (var type in assembly.ExportedTypes)
            {
                LoadIfExtensionType(type, locationHint);
            }
        }

        private bool LoadIfExtensionType(Type extensionType, string locationHint)
        {
            if (!typeof(IExtensionConfigProvider).IsAssignableFrom(extensionType))
            {
                return false;
            }

            if (IsExtensionLoaded(extensionType))
            {
                return false;
            }

            IExtensionConfigProvider instance = (IExtensionConfigProvider)Activator.CreateInstance(extensionType);
            LoadExtension(instance, locationHint);

            return true;
        }

        // Load extensions that are directly references by the user types.
        private void LoadDirectlyReferencesExtensions(IEnumerable<Type> userTypes)
        {
            var possibleExtensionAssemblies = UserTypeScanner.GetPossibleExtensionAssemblies(userTypes);

            foreach (var kv in possibleExtensionAssemblies)
            {
                var assembly = kv.Key;
                var locationHint = kv.Value;
                LoadExtensions(assembly, locationHint);
            }
        }

        private bool IsExtensionLoaded(Type type)
        {
            var registry = _config.HostConfig.GetService<IExtensionRegistry>();
            var extensions = registry.GetExtensions<IExtensionConfigProvider>();
            foreach (var extension in extensions)
            {
                var loadedExtentionType = extension.GetType();
                if (loadedExtentionType == type)
                {
                    return true;
                }
            }
            return false;
        }

        // Load a single extension
        private void LoadExtension(IExtensionConfigProvider instance, string locationHint = null)
        {
            JobHostConfiguration config = _config.HostConfig;

            var type = instance.GetType();
            string name = type.Name;

            string msg = $"Loaded custom extension: {name} from '{locationHint}'";
            _logger.LogInformation(msg);
            config.AddExtension(instance);
        }
    }
}
