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
    public class ScriptHost : JobHost
    {
        protected readonly JobHostConfiguration _jobHostConfig;
        protected readonly ScriptHostConfiguration _scriptHostConfig;

        protected ScriptHost(JobHostConfiguration config, ScriptHostConfiguration scriptConfig) 
            : base(config)
        {
            _jobHostConfig = config;
            _scriptHostConfig = scriptConfig;
        }

        protected virtual void Initialize()
        {
            List<FunctionDescriptorProvider> descriptionProviders = new List<FunctionDescriptorProvider>()
            {
                new ScriptFunctionDescriptorProvider(_scriptHostConfig.ApplicationRootPath),
                new NodeFunctionDescriptorProvider(_scriptHostConfig.ApplicationRootPath)
            };

            // read host.json and apply to JobHostConfiguration
            string hostConfigFilePath = Path.Combine(_scriptHostConfig.ApplicationRootPath, @"scripts\host.json");
            Console.WriteLine(string.Format("Reading host configuration file '{0}'", hostConfigFilePath));
            string json = File.ReadAllText(hostConfigFilePath);
            JObject hostConfig = JObject.Parse(json);
            ApplyConfiguration(hostConfig, _jobHostConfig);

            // read all script functions and apply to JobHostConfiguration
            Collection<FunctionDescriptor> functions = ReadFunctions(_scriptHostConfig, descriptionProviders);
            string defaultNamespace = "Host";
            string typeName = string.Format("{0}.{1}", defaultNamespace, "Functions");
            Type type = FunctionGenerator.Generate(typeName, functions);
            List<Type> types = new List<Type>();
            types.Add(type);

            _jobHostConfig.TypeLocator = new TypeLocator(types);
        }

        public static ScriptHost Create(ScriptHostConfiguration scriptConfig = null)
        {
            if (scriptConfig == null)
            {
                scriptConfig = new ScriptHostConfiguration()
                {
                    ApplicationRootPath = Environment.CurrentDirectory
                };
            }

            JobHostConfiguration config = new JobHostConfiguration();
            ScriptHost scriptHost = new ScriptHost(config, scriptConfig);
            scriptHost.Initialize();

            return scriptHost;
        }

        internal static Collection<FunctionDescriptor> ReadFunctions(ScriptHostConfiguration config, IEnumerable<FunctionDescriptorProvider> descriptionProviders)
        {
            string scriptRootPath = Path.Combine(config.ApplicationRootPath, "scripts");
            List<FunctionInfo> functionInfos = new List<FunctionInfo>();
            foreach (var scriptDir in Directory.EnumerateDirectories(scriptRootPath))
            {
                FunctionInfo functionInfo = new FunctionInfo();

                // read the function config
                string functionConfigPath = Path.Combine(scriptDir, "function.json");
                if (!File.Exists(functionConfigPath))
                {
                    // not a function directory
                    continue;
                }
                string json = File.ReadAllText(functionConfigPath);
                functionInfo.Configuration = JObject.Parse(json);

                // unless the name is explicitly set in the config,
                // default it to the function folder name
                string name = (string)functionInfo.Configuration["name"];
                if (string.IsNullOrEmpty(name))
                {
                    functionInfo.Name = Path.GetFileNameWithoutExtension(scriptDir);
                }

                // determine the primary script
                string[] functionFiles = Directory.EnumerateFiles(scriptDir).Where(p => Path.GetFileName(p).ToLowerInvariant() != "function.json").ToArray();
                if (functionFiles.Length == 0)
                {
                    continue;
                }
                else if (functionFiles.Length == 1)
                {
                    // if there is only a single file, that file is primary
                    functionInfo.Source = functionFiles[0];
                }
                else
                {
                    // if there is a "run" file, that file is primary
                    string functionPrimary = null;
                    functionPrimary = functionFiles.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant() == "run");
                    if (string.IsNullOrEmpty(functionPrimary))
                    {
                        // for Node, any index.js file is primary
                        functionPrimary = functionFiles.FirstOrDefault(p => Path.GetFileName(p).ToLowerInvariant() == "index.js");
                        if (string.IsNullOrEmpty(functionPrimary))
                        {
                            // finally, if there is an explicit primary file indicated
                            // in config, use it
                            JToken token = functionInfo.Configuration["source"];
                            if (token != null)
                            {
                                string sourceFileName = (string)token;
                                functionPrimary = Path.Combine(scriptDir, sourceFileName);
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(functionPrimary))
                    {
                        // TODO: should this be an error?
                        continue;
                    }
                    functionInfo.Source = functionPrimary;
                }

                functionInfos.Add(functionInfo);
            }

            var functions = ReadFunctions(functionInfos, descriptionProviders);
            return functions;
        }

        internal static Collection<FunctionDescriptor> ReadFunctions(List<FunctionInfo> functions, IEnumerable<FunctionDescriptorProvider> descriptorProviders)
        {
            Collection<FunctionDescriptor> functionDescriptors = new Collection<FunctionDescriptor>();
            foreach (FunctionInfo function in functions)
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
                    functionDescriptors.Add(descriptor);
                }
            }

            return functionDescriptors;
        }

        internal static void ApplyConfiguration(JObject config, JobHostConfiguration jobHostConfig)
        {
            JToken hostId = (JToken)config["id"];
            if (hostId == null)
            {
                throw new InvalidOperationException("An 'id' must be specified in the host configuration.");
            }
            jobHostConfig.HostId = (string)hostId;

            // Apply Queues configuration
            JObject configSection = (JObject)config["queues"];
            JToken value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("maxPollingInterval", out value))
                {
                    jobHostConfig.Queues.MaxPollingInterval = TimeSpan.FromMilliseconds((int)value);
                }
                if (configSection.TryGetValue("batchSize", out value))
                {
                    jobHostConfig.Queues.BatchSize = (int)value;
                }
                if (configSection.TryGetValue("maxDequeueCount", out value))
                {
                    jobHostConfig.Queues.MaxDequeueCount = (int)value;
                }
                if (configSection.TryGetValue("newBatchThreshold", out value))
                {
                    jobHostConfig.Queues.NewBatchThreshold = (int)value;
                }
            }

            // Apply Singleton configuration
            configSection = (JObject)config["singleton"];
            value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("lockPeriod", out value))
                {
                    jobHostConfig.Singleton.LockPeriod = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("listenerLockPeriod", out value))
                {
                    jobHostConfig.Singleton.ListenerLockPeriod = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("listenerLockRecoveryPollingInterval", out value))
                {
                    jobHostConfig.Singleton.ListenerLockRecoveryPollingInterval = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("lockAcquisitionTimeout", out value))
                {
                    jobHostConfig.Singleton.LockAcquisitionTimeout = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("lockAcquisitionPollingInterval", out value))
                {
                    jobHostConfig.Singleton.LockAcquisitionPollingInterval = TimeSpan.Parse((string)value);
                }
            }

            // Apply ServiceBus configuration
            ServiceBusConfiguration sbConfig = new ServiceBusConfiguration();
            configSection = (JObject)config["serviceBus"];
            value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("maxConcurrentCalls", out value))
                {
                    sbConfig.MessageOptions.MaxConcurrentCalls = (int)value;
                }
            }
            jobHostConfig.UseServiceBus(sbConfig);

            // Apply Tracing configuration
            configSection = (JObject)config["tracing"];
            if (configSection != null && configSection.TryGetValue("consoleLevel", out value))
            {
                TraceLevel consoleLevel;
                if (Enum.TryParse<TraceLevel>((string)value, true, out consoleLevel))
                {
                    jobHostConfig.Tracing.ConsoleLevel = consoleLevel;
                }
            }

            // Apply WebHooks configuration
            WebHooksConfiguration webHooksConfig = new WebHooksConfiguration();
            configSection = (JObject)config["webhooks"];
            if (configSection != null && configSection.TryGetValue("port", out value))
            {
                webHooksConfig = new WebHooksConfiguration((int)value);
            }
            jobHostConfig.UseWebHooks(webHooksConfig);

            jobHostConfig.UseTimers();
        }
    }
}
