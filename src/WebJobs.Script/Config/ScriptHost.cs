// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.ServiceBus;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptHost : JobHost
    {
        private const string HostAssemblyName = "ScriptHost";
        private readonly TraceWriter _traceWriter;

        protected ScriptHost(ScriptHostConfiguration scriptConfig) 
            : base(scriptConfig.HostConfig)
        {
            ScriptConfig = scriptConfig;
            _traceWriter = scriptConfig.TraceWriter;
        }

        public ScriptHostConfiguration ScriptConfig { get; private set; }

        public Collection<FunctionDescriptor> Functions { get; private set; }

        public async Task CallAsync(string method, Dictionary<string, object> arguments, CancellationToken cancellationToken = default(CancellationToken))
        {
            // TODO: Don't hardcode Functions Type name
            // TODO: Validate inputs
            // TODO: Cache this lookup result
            string typeName = "Functions";
            method = method.ToLowerInvariant();
            Type type = ScriptConfig.HostConfig.TypeLocator.GetTypes().SingleOrDefault(p => p.Name == typeName);
            MethodInfo methodInfo = type.GetMethods().SingleOrDefault(p => p.Name.ToLowerInvariant() == method);

            await CallAsync(methodInfo, arguments, cancellationToken);
        }

        protected virtual void Initialize()
        {
            List<FunctionDescriptorProvider> descriptionProviders = new List<FunctionDescriptorProvider>()
            {
                new ScriptFunctionDescriptorProvider(ScriptConfig),
                new NodeFunctionDescriptorProvider(this, ScriptConfig)
            };

            if (ScriptConfig.HostConfig.IsDevelopment)
            {
                ScriptConfig.HostConfig.UseDevelopmentSettings();
            }

            // read host.json and apply to JobHostConfiguration
            string hostConfigFilePath = Path.Combine(ScriptConfig.RootScriptPath, "host.json");
            _traceWriter.Verbose(string.Format("Reading host configuration file '{0}'", hostConfigFilePath));
            string json = File.ReadAllText(hostConfigFilePath);
            JObject hostConfig = JObject.Parse(json);
            ApplyConfiguration(hostConfig, ScriptConfig);

            // read all script functions and apply to JobHostConfiguration
            Collection<FunctionDescriptor> functions = ReadFunctions(ScriptConfig, descriptionProviders);
            string defaultNamespace = "Host";
            string typeName = string.Format("{0}.{1}", defaultNamespace, "Functions");
            _traceWriter.Verbose(string.Format("Generating {0} job function(s)", functions.Count));
            Type type = FunctionGenerator.Generate(HostAssemblyName, typeName, functions);
            List<Type> types = new List<Type>();
            types.Add(type);

            ScriptConfig.HostConfig.TypeLocator = new TypeLocator(types);
            ScriptConfig.HostConfig.NameResolver = new NameResolver();

            Functions = functions;
        }


        public static ScriptHost Create(ScriptHostConfiguration scriptConfig = null)
        {
            if (scriptConfig == null)
            {
                scriptConfig = new ScriptHostConfiguration();
            }

            scriptConfig.TraceWriter = scriptConfig.TraceWriter ?? new NullTraceWriter();

            if (!Path.IsPathRooted(scriptConfig.RootScriptPath))
            {
                scriptConfig.RootScriptPath = Path.Combine(Environment.CurrentDirectory, scriptConfig.RootScriptPath);
            }

            ScriptHost scriptHost = new ScriptHost(scriptConfig);
            scriptHost.Initialize();

            return scriptHost;
        }

        internal static Collection<FunctionDescriptor> ReadFunctions(ScriptHostConfiguration config, IEnumerable<FunctionDescriptorProvider> descriptionProviders)
        {
            string scriptRootPath = config.RootScriptPath;
            List<FunctionMetadata> metadatas = new List<FunctionMetadata>();
            foreach (var scriptDir in Directory.EnumerateDirectories(scriptRootPath))
            {
                FunctionMetadata metadata = new FunctionMetadata();

                // read the function config
                string functionConfigPath = Path.Combine(scriptDir, "function.json");
                if (!File.Exists(functionConfigPath))
                {
                    // not a function directory
                    continue;
                }
                string json = File.ReadAllText(functionConfigPath);
                metadata.Configuration = JObject.Parse(json);

                // unless the name is explicitly set in the config,
                // default it to the function folder name
                string name = (string)metadata.Configuration["name"];
                if (string.IsNullOrEmpty(name))
                {
                    metadata.Name = Path.GetFileNameWithoutExtension(scriptDir);
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
                    metadata.Source = functionFiles[0];
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
                            JToken token = metadata.Configuration["source"];
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
                    metadata.Source = functionPrimary;
                }

                metadatas.Add(metadata);
            }

            var functions = ReadFunctions(metadatas, descriptionProviders);
            return functions;
        }

        internal static Collection<FunctionDescriptor> ReadFunctions(List<FunctionMetadata> metadatas, IEnumerable<FunctionDescriptorProvider> descriptorProviders)
        {
            Collection<FunctionDescriptor> functionDescriptors = new Collection<FunctionDescriptor>();
            foreach (FunctionMetadata metadata in metadatas)
            {
                FunctionDescriptor descriptor = null;
                foreach (var provider in descriptorProviders)
                {
                    if (provider.TryCreate(metadata, out descriptor))
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

        internal static void ApplyConfiguration(JObject config, ScriptHostConfiguration scriptConfig)
        {
            JobHostConfiguration hostConfig = scriptConfig.HostConfig;

            JToken hostId = (JToken)config["id"];
            if (hostId == null)
            {
                throw new InvalidOperationException("An 'id' must be specified in the host configuration.");
            }
            hostConfig.HostId = (string)hostId;

            JToken watchFiles = (JToken)config["watchFiles"];
            if (watchFiles != null && watchFiles.Type == JTokenType.Boolean)
            {
                scriptConfig.WatchFiles = (bool)watchFiles;
            }

            // Apply Queues configuration
            JObject configSection = (JObject)config["queues"];
            JToken value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("maxPollingInterval", out value))
                {
                    hostConfig.Queues.MaxPollingInterval = TimeSpan.FromMilliseconds((int)value);
                }
                if (configSection.TryGetValue("batchSize", out value))
                {
                    hostConfig.Queues.BatchSize = (int)value;
                }
                if (configSection.TryGetValue("maxDequeueCount", out value))
                {
                    hostConfig.Queues.MaxDequeueCount = (int)value;
                }
                if (configSection.TryGetValue("newBatchThreshold", out value))
                {
                    hostConfig.Queues.NewBatchThreshold = (int)value;
                }
            }

            // Apply Singleton configuration
            configSection = (JObject)config["singleton"];
            value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("lockPeriod", out value))
                {
                    hostConfig.Singleton.LockPeriod = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("listenerLockPeriod", out value))
                {
                    hostConfig.Singleton.ListenerLockPeriod = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("listenerLockRecoveryPollingInterval", out value))
                {
                    hostConfig.Singleton.ListenerLockRecoveryPollingInterval = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("lockAcquisitionTimeout", out value))
                {
                    hostConfig.Singleton.LockAcquisitionTimeout = TimeSpan.Parse((string)value);
                }
                if (configSection.TryGetValue("lockAcquisitionPollingInterval", out value))
                {
                    hostConfig.Singleton.LockAcquisitionPollingInterval = TimeSpan.Parse((string)value);
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
            hostConfig.UseServiceBus(sbConfig);

            // Apply Tracing configuration
            configSection = (JObject)config["tracing"];
            if (configSection != null && configSection.TryGetValue("consoleLevel", out value))
            {
                TraceLevel consoleLevel;
                if (Enum.TryParse<TraceLevel>((string)value, true, out consoleLevel))
                {
                    hostConfig.Tracing.ConsoleLevel = consoleLevel;
                }
            }

            hostConfig.UseTimers();

            if (scriptConfig.TraceWriter != null)
            {
                hostConfig.Tracing.Tracers.Add(scriptConfig.TraceWriter);
            }
        }
    }
}
