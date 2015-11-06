// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Extensions.WebHooks;
using Microsoft.Azure.WebJobs.ServiceBus;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class Manifest
    {
        private readonly JObject _manifest;
        private readonly ScriptHostConfiguration _scriptHostConfig;
        private readonly Collection<FunctionDescriptor> _functions;

        private Manifest(ScriptHostConfiguration scriptHostConfig, Collection<FunctionDescriptor> functions, JObject manifest)
        {
            _scriptHostConfig = scriptHostConfig;
            _functions = functions;
            _manifest = manifest;
        }

        public static Manifest Read(ScriptHostConfiguration config, IEnumerable<FunctionDescriptorProvider> descriptionProviders)
        {
            string manifestFilePath = Path.Combine(config.ApplicationRootPath, @"scripts\manifest.json");
            Console.WriteLine(string.Format("Reading job manifest file '{0}'", manifestFilePath));
            string json = File.ReadAllText(manifestFilePath);

            JObject manifest = JObject.Parse(json);

            var functions = ReadFunctions(manifest, descriptionProviders);
            return new Manifest(config, functions, manifest);
        }

        public void Apply(JobHostConfiguration config)
        {
            var configuration = (JObject)_manifest["config"];
            ApplyConfiguration(configuration, config);

            List<Type> types = new List<Type>(_scriptHostConfig.HostAssembly.GetTypes());
            string defaultNamespace = "Host";
            if (types.Any())
            {
                defaultNamespace = types.First().Namespace;
            }
            string typeName = string.Format("{0}.{1}", defaultNamespace, "Functions");
            Type type = FunctionGenerator.Generate(typeName, _functions);
            types.Add(type);

            config.TypeLocator = new TypeLocator(types);
        }

        internal static Collection<FunctionDescriptor> ReadFunctions(JObject manifest, IEnumerable<FunctionDescriptorProvider> descriptorProviders)
        {
            JArray functionArray = (JArray)manifest["functions"];
            Collection<FunctionDescriptor> functions = new Collection<FunctionDescriptor>();
            foreach (JObject function in functionArray)
            {
                FunctionDescriptor descriptor = null;
                foreach (var provider in descriptorProviders)
                {
                    if (provider.TryCreate(function, out descriptor))
                    {
                        break;
                    }
                }

                if (descriptor != null)
                {
                    functions.Add(descriptor);
                }
            }

            return functions;
        }

        internal static void ApplyConfiguration(JObject manifest, JobHostConfiguration config)
        {
            JToken hostId = (JToken)manifest["id"];
            if (hostId == null)
            {
                throw new InvalidOperationException("An 'id' must be specified in the 'config' section of the manifest.");
            }
            config.HostId = (string)hostId;

            // Apply Queues configuration
            JObject configSection = (JObject)manifest["queues"];
            JToken value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("maxPollingInterval", out value))
                {
                    config.Queues.MaxPollingInterval = TimeSpan.FromMilliseconds((int)value);
                }
                if (configSection.TryGetValue("batchSize", out value))
                {
                    config.Queues.BatchSize = (int)value;
                }
                if (configSection.TryGetValue("maxDequeueCount", out value))
                {
                    config.Queues.MaxDequeueCount = (int)value;
                }
                if (configSection.TryGetValue("newBatchThreshold", out value))
                {
                    config.Queues.NewBatchThreshold = (int)value;
                }
            }

            // Apply Singleton configuration
            configSection = (JObject)manifest["singleton"];
            value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("lockPeriod", out value))
                {
                    config.Singleton.LockPeriod = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("listenerLockPeriod", out value))
                {
                    config.Singleton.ListenerLockPeriod = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("listenerLockRecoveryPollingInterval", out value))
                {
                    config.Singleton.ListenerLockRecoveryPollingInterval = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("lockAcquisitionTimeout", out value))
                {
                    config.Singleton.LockAcquisitionTimeout = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("lockAcquisitionPollingInterval", out value))
                {
                    config.Singleton.LockAcquisitionPollingInterval = TimeSpan.Parse((string)value);
                }
            }

            // Apply ServiceBus configuration
            ServiceBusConfiguration sbConfig = new ServiceBusConfiguration();
            configSection = (JObject)manifest["serviceBus"];
            value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("maxConcurrentCalls", out value))
                {
                    sbConfig.MessageOptions.MaxConcurrentCalls = (int)value;
                }
            }
            config.UseServiceBus(sbConfig);

            // Apply Tracing configuration
            configSection = (JObject)manifest["tracing"];
            if (configSection != null && configSection.TryGetValue("consoleLevel", out value))
            {
                TraceLevel consoleLevel;
                if (Enum.TryParse<TraceLevel>((string)value, true, out consoleLevel))
                {
                    config.Tracing.ConsoleLevel = consoleLevel;
                }
            }

            // Apply WebHooks configuration
            WebHooksConfiguration webHooksConfig = new WebHooksConfiguration();
            configSection = (JObject)manifest["webhooks"];
            if (configSection != null && configSection.TryGetValue("port", out value))
            {
                webHooksConfig = new WebHooksConfiguration((int)value);
            }
            config.UseWebHooks(webHooksConfig);

            config.UseTimers();
        }
    }
}
