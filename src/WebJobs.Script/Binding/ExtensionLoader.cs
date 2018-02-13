// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class ExtensionLoader
    {
        private readonly TraceWriter _traceWriter;
        private readonly ILogger _startupLogger;
        private readonly ScriptHostConfiguration _config;
        private readonly bool _dynamicExtensionLoadingEnabled;

        // The set of "built in" binding types. These are types that are directly accesible without
        // needing an explicit load gesture.
        private static IReadOnlyDictionary<string, string> _builtinBindingTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "bot",                "Microsoft.Azure.WebJobs.Extensions.BotFramework.Config.BotFrameworkConfiguration, Microsoft.Azure.WebJobs.Extensions.BotFramework" },
            { "sendgrid",           "Microsoft.Azure.WebJobs.Extensions.SendGrid.SendGridConfiguration, Microsoft.Azure.WebJobs.Extensions.SendGrid" },
            { "eventGridTrigger",   "Microsoft.Azure.WebJobs.Extensions.EventGrid.EventGridExtensionConfig, Microsoft.Azure.WebJobs.Extensions.EventGrid" }
        };

        // The set of "built in" binding types that are built on the older/obsolete ScriptBindingProvider APIs
        // Over time these will migrate to the above set as they are reworked.
        private static IReadOnlyDictionary<string, string> _builtinScriptBindingTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "twilioSms",          "Microsoft.Azure.WebJobs.Script.Binding.TwilioScriptBindingProvider" },
            { "notificationHub",    "Microsoft.Azure.WebJobs.Script.Binding.NotificationHubScriptBindingProvider" },
            { "cosmosDBTrigger",    "Microsoft.Azure.WebJobs.Script.Binding.DocumentDBScriptBindingProvider" },
            { "documentDB",         "Microsoft.Azure.WebJobs.Script.Binding.DocumentDBScriptBindingProvider" },
            { "mobileTable",        "Microsoft.Azure.WebJobs.Script.Binding.MobileAppsScriptBindingProvider" },
            { "apiHubFileTrigger",  "Microsoft.Azure.WebJobs.Script.Binding.ApiHubScriptBindingProvider" },
            { "apiHubFile",         "Microsoft.Azure.WebJobs.Script.Binding.ApiHubScriptBindingProvider" },
            { "apiHubTable",        "Microsoft.Azure.WebJobs.Script.Binding.ApiHubScriptBindingProvider" },
            { "serviceBusTrigger",  "Microsoft.Azure.WebJobs.Script.Binding.ServiceBusScriptBindingProvider" },
            { "serviceBus",         "Microsoft.Azure.WebJobs.Script.Binding.ServiceBusScriptBindingProvider" },
            { "eventHubTrigger",    "Microsoft.Azure.WebJobs.Script.Binding.ServiceBusScriptBindingProvider" },
            { "eventHub",           "Microsoft.Azure.WebJobs.Script.Binding.ServiceBusScriptBindingProvider" },
        };

        public ExtensionLoader(ScriptHostConfiguration config, TraceWriter traceWriter, ILogger startupLogger)
        {
            _config = config;
            _traceWriter = traceWriter;
            _startupLogger = startupLogger;
            _dynamicExtensionLoadingEnabled = FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagsEnableDynamicExtensionLoading);
        }

        /// <summary>
        /// Get the ScriptBindingProviderType for a given binding type.
        /// </summary>
        /// <param name="bindingTypeName">The Type name of the binding.</param>
        /// <returns>The binding provider Type, or null.</returns>
        public static Type GetScriptBindingProvider(string bindingTypeName)
        {
            string assemblyQualifiedTypeName;
            if (_builtinScriptBindingTypes.TryGetValue(bindingTypeName, out assemblyQualifiedTypeName))
            {
                var type = Type.GetType(assemblyQualifiedTypeName);
                return type;
            }
            return null;
        }

        public void LoadCustomExtensions()
        {
            string extensionsPath = _config.RootExtensionsPath;
            if (!string.IsNullOrWhiteSpace(extensionsPath))
            {
                foreach (var dir in Directory.EnumerateDirectories(extensionsPath))
                {
                    LoadCustomExtensions(dir);
                }
            }
        }

        private void LoadCustomExtensions(string extensionsPath)
        {
            foreach (var path in Directory.EnumerateFiles(extensionsPath, "*.dll"))
            {
                // We don't want to load and reflect over every dll.
                // By convention, restrict to based on filenames.
                var filename = Path.GetFileName(path);
                if (!filename.ToLowerInvariant().Contains("extension"))
                {
                    continue;
                }

                try
                {
                    // See GetNugetPackagesPath() for details
                    // Script runtime is already setup with assembly resolution hooks, so use LoadFrom
                    Assembly assembly = Assembly.LoadFrom(path);
                    LoadExtensions(assembly, path);
                }
                catch (Exception e)
                {
                    string msg = $"Failed to load custom extension from '{path}'.";
                    WriteTraceEvent(msg, ex: e);
                    _startupLogger.LogError(0, e, msg);
                }
            }
        }

        private void WriteTraceEvent(string msg, TraceLevel traceLevel = TraceLevel.Info, Exception ex = null)
        {
            TraceEvent traceEvent;
            if (ex != null)
            {
                traceEvent = new TraceEvent(TraceLevel.Error, msg, GetType().Name, ex);
            }
            else
            {
                traceEvent = new TraceEvent(traceLevel, msg, GetType().Name);
            }
            traceEvent.Properties.Add(ScriptConstants.TracePropertyHostIdKey, _config.HostConfig.HostId);
            _traceWriter.Trace(traceEvent);
        }

        // Load extensions that are directly referenced by the user types.
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
            foreach (var type in assembly.ExportedTypes)
            {
                if (!typeof(IExtensionConfigProvider).IsAssignableFrom(type))
                {
                    continue;
                }

                if (IsExtensionLoaded(type))
                {
                    continue;
                }

                var extensionConfigProvider = (IExtensionConfigProvider)Activator.CreateInstance(type);
                LoadExtension(extensionConfigProvider, locationHint);
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

        private void LoadExtension(IExtensionConfigProvider extensionConfigProvider, string locationHint = null)
        {
            var extensionConfigProviderType = extensionConfigProvider.GetType();
            string extensionName = extensionConfigProviderType.Name;

            string msg = null;
            if (!string.IsNullOrEmpty(locationHint))
            {
                msg = $"Loaded binding extension '{extensionName}' from '{locationHint}'";
            }
            else
            {
                msg = $"Loaded custom extension '{extensionName}'";
            }

            WriteTraceEvent(msg);
            _startupLogger.LogInformation(msg);
            _config.HostConfig.AddExtension(extensionConfigProvider);
        }

        public void LoadBuiltinExtensions(IEnumerable<string> bindingTypes)
        {
            foreach (var bindingType in bindingTypes)
            {
                string assemblyQualifiedTypeName;
                if (_builtinBindingTypes.TryGetValue(bindingType, out assemblyQualifiedTypeName))
                {
                    Type typeExtension = Type.GetType(assemblyQualifiedTypeName);
                    if (typeExtension == null)
                    {
                        string errorMsg = $"Can't find binding provider '{assemblyQualifiedTypeName}' for '{bindingType}'";
                        WriteTraceEvent(errorMsg, TraceLevel.Error);
                        _startupLogger?.LogError(errorMsg);
                    }
                    else
                    {
                        IExtensionConfigProvider extension = (IExtensionConfigProvider)Activator.CreateInstance(typeExtension);
                        LoadExtension(extension);
                    }
                }
            }
        }

        public IEnumerable<string> DiscoverBindingTypes(IEnumerable<FunctionMetadata> functions)
        {
            if (_dynamicExtensionLoadingEnabled)
            {
                // only load binding extensions that are actually used
                var bindingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var function in functions)
                {
                    foreach (var binding in function.InputBindings.Concat(function.OutputBindings))
                    {
                        bindingTypes.Add(binding.Type);
                    }
                }

                return bindingTypes;
            }
            else
            {
                // if not enabled, load all binding extensions
                return _builtinBindingTypes.Keys.Concat(_builtinScriptBindingTypes.Keys).ToArray();
            }
        }
    }
}
