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
        private const string HostAssemblyName = "ScriptHost";
        private FileSystemWatcher _fileWatcher;

        protected ScriptHost(ScriptHostConfiguration scriptConfig) 
            : base(scriptConfig.HostConfig)
        {
            ScriptConfig = scriptConfig;
        }

        public ScriptHostConfiguration ScriptConfig { get; private set; }

        public bool Restart { get; private set; }

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
            string hostConfigFilePath = Path.Combine(ScriptConfig.RootPath, "host.json");
            Console.WriteLine(string.Format("Reading host configuration file '{0}'", hostConfigFilePath));
            string json = File.ReadAllText(hostConfigFilePath);
            JObject hostConfig = JObject.Parse(json);
            ApplyConfiguration(hostConfig, ScriptConfig);

            // read all script functions and apply to JobHostConfiguration
            Collection<FunctionDescriptor> functions = ReadFunctions(ScriptConfig, descriptionProviders);
            string defaultNamespace = "Host";
            string typeName = string.Format("{0}.{1}", defaultNamespace, "Functions");
            Console.WriteLine(string.Format("Generating {0} job function(s)", functions.Count));
            Type type = FunctionGenerator.Generate(HostAssemblyName, typeName, functions);
            List<Type> types = new List<Type>();
            types.Add(type);

            ScriptConfig.HostConfig.TypeLocator = new TypeLocator(types);
            ScriptConfig.HostConfig.NameResolver = new NameResolver();

            if (ScriptConfig.WatchFiles)
            {
                _fileWatcher = new FileSystemWatcher(ScriptConfig.RootPath, "*.json")
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                _fileWatcher.Changed += OnConfigurationFileChanged;
            }
        }

        private void StopAndRestart()
        {
            if (Restart)
            {
                // we've already received a restart call
                return;
            }

            Console.WriteLine("The host configuration file has changed. Restarting.");

            // Flag for restart and stop the host.
            Restart = true;
            Stop();
        }

        public static ScriptHost Create(ScriptHostConfiguration scriptConfig = null)
        {
            if (scriptConfig == null)
            {
                scriptConfig = new ScriptHostConfiguration()
                {
                    RootPath = Environment.CurrentDirectory
                };
            }

            if (!Path.IsPathRooted(scriptConfig.RootPath))
            {
                scriptConfig.RootPath = Path.Combine(Environment.CurrentDirectory, scriptConfig.RootPath);
            }

            ScriptHost scriptHost = new ScriptHost(scriptConfig);
            scriptHost.Initialize();

            return scriptHost;
        }

        internal static Collection<FunctionDescriptor> ReadFunctions(ScriptHostConfiguration config, IEnumerable<FunctionDescriptorProvider> descriptionProviders)
        {
            string scriptRootPath = config.RootPath;
            List<FunctionFolderInfo> functionFolderInfos = new List<FunctionFolderInfo>();
            foreach (var scriptDir in Directory.EnumerateDirectories(scriptRootPath))
            {
                FunctionFolderInfo functionInfo = new FunctionFolderInfo();

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

                functionFolderInfos.Add(functionInfo);
            }

            var functions = ReadFunctions(functionFolderInfos, descriptionProviders);
            return functions;
        }

        internal static Collection<FunctionDescriptor> ReadFunctions(List<FunctionFolderInfo> functionFolderInfos, IEnumerable<FunctionDescriptorProvider> descriptorProviders)
        {
            Collection<FunctionDescriptor> functionDescriptors = new Collection<FunctionDescriptor>();
            foreach (FunctionFolderInfo functionFolderInfo in functionFolderInfos)
            {
                FunctionDescriptor descriptor = null;
                foreach (var provider in descriptorProviders)
                {
                    if (provider.TryCreate(functionFolderInfo, out descriptor))
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

            // Apply WebHooks configuration
            WebHooksConfiguration webHooksConfig = new WebHooksConfiguration();
            configSection = (JObject)config["webhooks"];
            if (configSection != null && configSection.TryGetValue("port", out value))
            {
                webHooksConfig = new WebHooksConfiguration((int)value);
            }
            hostConfig.UseWebHooks(webHooksConfig);

            hostConfig.UseTimers();
        }

        private void OnConfigurationFileChanged(object sender, FileSystemEventArgs e)
        {
            string fileName = Path.GetFileName(e.Name);

            if (!Restart &&
                ((string.Compare(fileName, "host.json") == 0) || string.Compare(fileName, "function.json") == 0))
            {
                StopAndRestart();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fileWatcher != null)
                {
                    _fileWatcher.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
