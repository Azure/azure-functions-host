// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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

        public static Manifest Read(ScriptConfiguration config, IEnumerable<FunctionDescriptorProvider> descriptionProviders)
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

        private static Collection<FunctionDescriptor> ReadFunctions(JObject manifest, ScriptConfiguration config, IEnumerable<FunctionDescriptorProvider> descriptionProviders)
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
            JObject queuesConfig = (JObject)Configuration["queues"];
            JToken value = null;
            if (queuesConfig.TryGetValue("maxPollingInterval", out value))
            {
                config.Queues.MaxPollingInterval = TimeSpan.FromMilliseconds((int)value);
            }
            if (queuesConfig.TryGetValue("batchSize", out value))
            {
                config.Queues.BatchSize = (int)value;
            }
            if (queuesConfig.TryGetValue("maxDequeueCount", out value))
            {
                config.Queues.MaxDequeueCount = (int)value;
            }
            if (queuesConfig.TryGetValue("newBatchThreshold", out value))
            {
                config.Queues.NewBatchThreshold = (int)value;
            }

            JObject tracingConfig = (JObject)Configuration["tracing"];
            if (tracingConfig.TryGetValue("consoleLevel", out value))
            {
                TraceLevel consoleLevel;
                if (Enum.TryParse<TraceLevel>((string)value, out consoleLevel))
                {
                    config.Tracing.ConsoleLevel = consoleLevel;
                }
            }
        }
    }
}
