// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class LanguageWorkerChannelManager : ILanguageWorkerChannelManager
    {
        private readonly IEnumerable<WorkerConfig> _workerConfigs = null;
        private readonly ILogger _logger = null;
        private readonly TimeSpan processStartTimeout = TimeSpan.FromSeconds(40);
        private readonly TimeSpan workerInitTimeout = TimeSpan.FromSeconds(30);
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions = null;
        private readonly ILanguageWorkerConsoleLogSource _consoleLogSource;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IEnvironment _environment;
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IWorkerProcessFactory _processFactory;
        private readonly IProcessRegistry _processRegistry;
        private readonly IRpcServer _rpcServer = null;
        private readonly IDisposable _rpcChannelReadySubscriptions;
        private string _workerRuntime;
        private Action _shutdownStandbyWorkerChannels;
        private IDictionary<string, ILanguageWorkerChannel> _workerChannels = new Dictionary<string, ILanguageWorkerChannel>();
        private IDictionary<string, ILanguageWorkerProcess> _workerProcesses = new Dictionary<string, ILanguageWorkerProcess>();

        public LanguageWorkerChannelManager(IScriptEventManager eventManager, IEnvironment environment, IRpcServer rpcServer, ILoggerFactory loggerFactory, IOptions<LanguageWorkerOptions> languageWorkerOptions,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, ILanguageWorkerConsoleLogSource consoleLogSource)
        {
            _rpcServer = rpcServer;
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryLanguageWorkerChannelManager);
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _applicationHostOptions = applicationHostOptions;
            _consoleLogSource = consoleLogSource;

            _processFactory = new DefaultWorkerProcessFactory();
            try
            {
                _processRegistry = ProcessRegistryFactory.Create();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Unable to create process registry");
            }

            _shutdownStandbyWorkerChannels = ScheduleShutdownStandbyChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(5000);
            _rpcChannelReadySubscriptions = _eventManager.OfType<RpcWebHostChannelReadyEvent>()
               .Subscribe(AddOrUpdateWorkerChannels);
        }

        public ILanguageWorkerChannel CreateLanguageWorkerChannel(string workerId, string scriptRootPath, string language, IObservable<FunctionRegistrationContext> functionRegistrations, IMetricsLogger metricsLogger, int attemptCount, bool isWebhostChannel = false)
        {
            WorkerConfig languageWorkerConfig = GetWorkerConfig(language);
            return new LanguageWorkerChannel(
                         workerId,
                         scriptRootPath,
                         _eventManager,
                         functionRegistrations,
                         languageWorkerConfig,
                         _rpcServer.Uri,
                         _loggerFactory,
                         metricsLogger,
                         attemptCount,
                         isWebhostChannel);
        }

        private WorkerConfig GetWorkerConfig(string language)
        {
            var languageWorkerConfig = _workerConfigs.Where(c => c.Language.Equals(language, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (languageWorkerConfig == null)
            {
                throw new InvalidOperationException($"WorkerCofig for runtime: {language} not found");
            }

            return languageWorkerConfig;
        }

        public async Task InitializeChannelAsync(string runtime)
        {
            _logger?.LogDebug("Initializing language worker channel for runtime:{runtime}", runtime);
            await InitializeLanguageWorkerChannel(runtime, _applicationHostOptions.CurrentValue.ScriptPath);
        }

        private async Task InitializeLanguageWorkerChannel(string language, string scriptRootPath)
        {
            try
            {
                string workerId = Guid.NewGuid().ToString();
                _logger.LogInformation("Creating language worker channel for runtime:{runtime}", language);
                ILanguageWorkerChannel languageWorkerChannel = CreateLanguageWorkerChannel(workerId, scriptRootPath, language, null, null, 0, true);

                var languageWorkerProcess = StartWorkerProcess(workerId, language, scriptRootPath);
                _workerProcesses.Add(language, languageWorkerProcess);

                IObservable<RpcWebHostChannelReadyEvent> rpcChannelReadyEvent = _eventManager.OfType<RpcWebHostChannelReadyEvent>()
                                                                        .Where(msg => msg.Language == language).Timeout(workerInitTimeout);
                // Wait for response from language worker process
                RpcWebHostChannelReadyEvent readyEvent = await rpcChannelReadyEvent.FirstAsync();
            }
            catch (Exception ex)
            {
                throw new HostInitializationException($"Failed to start Language Worker Channel for language :{language}", ex);
            }
        }

        public ILanguageWorkerProcess StartWorkerProcess(string workerId, string runtime, string rootScriptPath)
        {
            var workerConfig = GetWorkerConfig(runtime);
            var workerContext = new WorkerContext()
            {
                RequestId = Guid.NewGuid().ToString(),
                MaxMessageLength = LanguageWorkerConstants.DefaultMaxMessageLengthBytes,
                WorkerId = workerId,
                Arguments = workerConfig.Arguments,
                WorkingDirectory = rootScriptPath,
                ServerUri = _rpcServer.Uri,
            };

            var languageWorkerProcess = new LanguageWorkerProcess(workerConfig.Language, workerId, workerContext, _eventManager, _processFactory, _processRegistry, _loggerFactory, _consoleLogSource);
            languageWorkerProcess.StartProcess();
            return languageWorkerProcess;
        }

        public ILanguageWorkerChannel GetChannel(string language)
        {
            ILanguageWorkerChannel initializedChannel = null;
            if (!string.IsNullOrEmpty(language) && _workerChannels.TryGetValue(language, out initializedChannel))
            {
                return initializedChannel;
            }
            return null;
        }

        public async Task SpecializeAsync()
        {
            _logger.LogInformation(Resources.LanguageWorkerChannelSpecializationTrace);
            _workerRuntime = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName);
            ILanguageWorkerChannel languageWorkerChannel = GetChannel(_workerRuntime);
            if (_workerRuntime != null && languageWorkerChannel != null)
            {
                _logger.LogInformation("Loading environment variables for runtime: {runtime}", _workerRuntime);
                IObservable<WorkerProcessReadyEvent> processReadyEvents = _eventManager.OfType<WorkerProcessReadyEvent>()
                .Where(msg => string.Equals(msg.Language, _workerRuntime, StringComparison.OrdinalIgnoreCase))
                .Timeout(workerInitTimeout);
                languageWorkerChannel.SendFunctionEnvironmentReloadRequest();

                // Wait for response from language worker process
                await processReadyEvents.FirstAsync();
            }
            _shutdownStandbyWorkerChannels();
        }

        public bool ShutdownChannelIfExists(string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                throw new ArgumentNullException(nameof(language));
            }
            ILanguageWorkerChannel initializedChannel = null;
            if (_workerChannels.TryGetValue(language, out initializedChannel))
            {
                initializedChannel.Dispose();
                _workerChannels.Remove(language);
                return true;
            }
            ILanguageWorkerProcess initializedProcess = null;
            if (_workerProcesses.TryGetValue(language, out initializedProcess))
            {
                initializedProcess.Dispose();
                _workerProcesses.Remove(language);
                return true;
            }

            return false;
        }

        internal void ScheduleShutdownStandbyChannels()
        {
            _workerRuntime = _workerRuntime ?? _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName);
            if (!string.IsNullOrEmpty(_workerRuntime))
            {
                var standbyChannels = _workerChannels.Where(ch => ch.Key.ToLower() != _workerRuntime.ToLower()).ToList();
                for (int i = 0; i < standbyChannels.Count(); i++)
                {
                    _logger.LogInformation("Disposing standby channel for runtime:{language}", standbyChannels.ElementAt(i).Key);
                    standbyChannels.ElementAt(i).Value.Dispose();
                    _workerChannels.Remove(standbyChannels.ElementAt(i).Key);
                }
                var standbyProcesses = _workerProcesses.Where(ch => ch.Key.ToLower() != _workerRuntime.ToLower()).ToList();
                for (int i = 0; i < standbyProcesses.Count(); i++)
                {
                    _logger.LogInformation("Disposing standby worker process for runtime:{language}", standbyProcesses.ElementAt(i).Key);
                    standbyProcesses.ElementAt(i).Value.Dispose();
                    _workerProcesses.Remove(standbyChannels.ElementAt(i).Key);
                }
            }
        }

        public void ShutdownStandbyChannels(IEnumerable<FunctionMetadata> functions)
        {
            if (_environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode) == "1")
            {
                return;
            }
            _workerRuntime = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName) ?? Utility.GetWorkerRuntime(functions);
            _logger.LogInformation("WorkerRuntime: {workerRuntime}. Will shutdown other standby channels", _workerRuntime);
            if (string.IsNullOrEmpty(_workerRuntime))
            {
                ShutdownChannels();
                return;
            }
            else
            {
                ScheduleShutdownStandbyChannels();
            }
        }

        public void ShutdownChannels()
        {
            foreach (string runtime in _workerChannels.Keys.ToList())
            {
                _logger.LogInformation("Shutting down language worker channel for runtime:{runtime}", runtime);
                if (_workerChannels[runtime] != null)
                {
                    _workerChannels[runtime].Dispose();
                }
            }
            _workerChannels.Clear();

            foreach (string runtime in _workerProcesses.Keys.ToList())
            {
                _logger.LogInformation("Shutting down language worker process for runtime:{runtime}", runtime);
                if (_workerProcesses[runtime] != null)
                {
                    _workerProcesses[runtime].Dispose();
                }
            }
            _workerProcesses.Clear();
            (_processRegistry as IDisposable)?.Dispose();
        }

        private void AddOrUpdateWorkerChannels(RpcWebHostChannelReadyEvent rpcChannelReadyEvent)
        {
            _logger.LogInformation("Adding language worker channel for runtime: {language}.", rpcChannelReadyEvent.Language);
            _workerChannels.Add(rpcChannelReadyEvent.Language, rpcChannelReadyEvent.LanguageWorkerChannel);
        }
    }
}
