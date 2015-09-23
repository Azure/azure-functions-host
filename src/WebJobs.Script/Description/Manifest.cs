// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Extensions.WebHooks;
using Microsoft.Azure.WebJobs.ServiceBus;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class Manifest
    {

        private Manifest()
        {
        }

        private JObject Configuration { get; set; }

        private Collection<FunctionDescriptor> Functions { get; set; }

        public static Manifest Read(ScriptHostConfiguration config, IEnumerable<FunctionDescriptorProvider> descriptionProviders)
        {
            string manifestFilePath = Path.Combine(config.ApplicationRootPath, @"scripts\manifest.json");
            Console.WriteLine(string.Format("Reading job manifest file '{0}'", manifestFilePath));
            string json = File.ReadAllText(manifestFilePath);

            JObject manifest = JObject.Parse(json);

            return new Manifest
            {
                Functions = ReadFunctions(manifest, config, descriptionProviders),
                Configuration = (JObject)manifest["config"]
            };
        }

        public void Apply(JobHostConfiguration config)
        {
            ApplyConfiguration(config);

            Type type = FunctionGenerator.Generate(Functions);
            config.TypeLocator = new TypeLocator(type);
        }

        private static Collection<FunctionDescriptor> ReadFunctions(JObject manifest, ScriptHostConfiguration config, IEnumerable<FunctionDescriptorProvider> descriptionProviders)
        {
            JArray functionArray = (JArray)manifest["functions"];
            Collection<FunctionDescriptor> functions = new Collection<FunctionDescriptor>();
            foreach (JObject function in functionArray)
            {
                FunctionDescriptor descriptor = null;
                foreach (var provider in descriptionProviders)
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

        private void ApplyConfiguration(JobHostConfiguration config)
        {
            // Apply Queues configuration
            JObject configSection = (JObject)Configuration["queues"];
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

            // Apply ServiceBus configuration
            ServiceBusConfiguration sbConfig = new ServiceBusConfiguration();
            configSection = (JObject)Configuration["serviceBus"];
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
            configSection = (JObject)Configuration["tracing"];
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
            configSection = (JObject)Configuration["webhooks"];
            if (configSection != null && configSection.TryGetValue("port", out value))
            {
                webHooksConfig = new WebHooksConfiguration((int)value);
            }
            config.UseWebHooks(webHooksConfig);

            config.UseTimers();
        }
    }
}
