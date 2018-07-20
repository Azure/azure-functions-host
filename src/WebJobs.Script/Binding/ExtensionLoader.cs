// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class ExtensionLoader
    {
        private readonly ScriptHostOptions _config;
        private readonly IExtensionRegistry _extensionRegistry;
        private readonly ILogger _logger;

        public ExtensionLoader(ScriptHostOptions config, IExtensionRegistry extensionRegistry, ILogger logger)
        {
            _config = config;
            _extensionRegistry = extensionRegistry;
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

        private void LoadExtensions(Assembly assembly, string locationHint)
        {
            foreach (var type in assembly.ExportedTypes.Where(p => !p.IsAbstract))
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
            var extensions = _extensionRegistry.GetExtensions<IExtensionConfigProvider>();
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
            // TODO: DI (FACAVAL) Extension loading
            //JobHostOptions config = _config.HostOptions;

            //var type = instance.GetType();
            //string name = type.Name;

            //string msg = $"Loaded custom extension: {name} from '{locationHint}'";
            //_logger.LogInformation(msg);
            // TODO: DI (FACAVAL) Inject extensions instead of using the extension registry
            //_extensionRegistry.
            //config.AddExtension(instance);
        }
    }
}
