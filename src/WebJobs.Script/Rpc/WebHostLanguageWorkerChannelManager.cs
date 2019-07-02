// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class WebHostLanguageWorkerChannelManager : IWebHostLanguageWorkerChannelManager
    {
        private readonly ILogger _logger = null;
        private readonly TimeSpan workerInitTimeout = TimeSpan.FromSeconds(30);
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions = null;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IEnvironment _environment;
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly ILanguageWorkerChannelFactory _languageWorkerChannelFactory;
        private string _workerRuntime;
        private Action _shutdownStandbyWorkerChannels;

        private ConcurrentDictionary<string, List<ILanguageWorkerChannel>> _workerChannels = new ConcurrentDictionary<string, List<ILanguageWorkerChannel>>();
        // Keeps environment config from placeholder mode that must be consistent for placeholders to run correctly
        private Dictionary<string, Dictionary<string, string>> _placeholderEnvironmentConfig = new Dictionary<string, Dictionary<string, string>>();

        public WebHostLanguageWorkerChannelManager(IScriptEventManager eventManager, IEnvironment environment, ILoggerFactory loggerFactory, ILanguageWorkerChannelFactory languageWorkerChannelFactory, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
            _languageWorkerChannelFactory = languageWorkerChannelFactory;
            _logger = loggerFactory.CreateLogger<WebHostLanguageWorkerChannelManager>();
            _applicationHostOptions = applicationHostOptions;

            _shutdownStandbyWorkerChannels = ScheduleShutdownStandbyChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(5000);

            // Set up initial checks for environment variables
            _placeholderEnvironmentConfig.Add(LanguageWorkerConstants.NodeLanguageWorkerName, new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionsNodeVersionSetting, _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionsNodeVersionSetting) }
            });
        }

        public Task<ILanguageWorkerChannel> InitializeChannelAsync(string runtime)
        {
            _logger?.LogDebug("Initializing language worker channel for runtime:{runtime}", runtime);
            return InitializeLanguageWorkerChannel(runtime, _applicationHostOptions.CurrentValue.ScriptPath);
        }

        private async Task<ILanguageWorkerChannel> InitializeLanguageWorkerChannel(string runtime, string scriptRootPath)
        {
            ILanguageWorkerChannel languageWorkerChannel = null;
            string workerId = Guid.NewGuid().ToString();
            _logger.LogDebug("Creating language worker channel for runtime:{runtime}", runtime);
            try
            {
                languageWorkerChannel = _languageWorkerChannelFactory.CreateLanguageWorkerChannel(scriptRootPath, runtime, null, 0, true);
                await languageWorkerChannel.StartWorkerProcessAsync();
                IObservable<RpcWebHostChannelReadyEvent> rpcChannelReadyEvent = _eventManager.OfType<RpcWebHostChannelReadyEvent>()
                                                                        .Where(msg => msg.Language == runtime).Timeout(workerInitTimeout);
                // Wait for response from language worker process
                RpcWebHostChannelReadyEvent readyEvent = await rpcChannelReadyEvent.FirstAsync();
                AddOrUpdateWorkerChannels(readyEvent);
            }
            catch (Exception ex)
            {
                throw new HostInitializationException($"Failed to start Language Worker Channel for language :{runtime}", ex);
            }
            return languageWorkerChannel;
        }

        internal ILanguageWorkerChannel GetChannel(string language)
        {
            if (!string.IsNullOrEmpty(language) && _workerChannels.TryGetValue(language, out List<ILanguageWorkerChannel> workerChannels))
            {
                return workerChannels.FirstOrDefault();
            }
            return null;
        }

        public IEnumerable<ILanguageWorkerChannel> GetChannels(string language)
        {
            if (!string.IsNullOrEmpty(language) && _workerChannels.TryGetValue(language, out List<ILanguageWorkerChannel> workerChannels))
            {
                return workerChannels;
            }
            return null;
        }

        public async Task SpecializeAsync()
        {
            _logger.LogInformation("Starting language worker channel specialization");
            _workerRuntime = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName);
            ILanguageWorkerChannel languageWorkerChannel = GetChannel(_workerRuntime);
            if (_workerRuntime != null && languageWorkerChannel != null)
            {
                _logger.LogInformation("Loading environment variables for runtime: {runtime}", _workerRuntime);
                await languageWorkerChannel.SendFunctionEnvironmentReloadRequest();
            }
            _shutdownStandbyWorkerChannels();
        }

        public bool ShutdownChannelIfExists(string language, string workerId)
        {
            if (string.IsNullOrEmpty(language))
            {
                throw new ArgumentNullException(nameof(language));
            }
            if (_workerChannels.TryGetValue(language, out List<ILanguageWorkerChannel> languageWorkerChannels))
            {
                var channel = languageWorkerChannels.FirstOrDefault(ch => ch.Id == workerId);
                if (channel != null)
                {
                    (channel as IDisposable)?.Dispose();
                    languageWorkerChannels.Remove(channel);
                    return true;
                }
            }
            return false;
        }

        internal void ScheduleShutdownStandbyChannels()
        {
            _workerRuntime = _workerRuntime ?? _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName);
            if (!string.IsNullOrEmpty(_workerRuntime))
            {
                var standbyWorkerChannels = _workerChannels.Where(ch => !ch.Key.Equals(_workerRuntime, StringComparison.InvariantCultureIgnoreCase));
                foreach (var runtime in standbyWorkerChannels)
                {
                    _logger.LogInformation("Disposing standby channel for runtime:{language}", runtime.Key);

                    if (_workerChannels.TryRemove(runtime.Key, out List<ILanguageWorkerChannel> standbyChannels))
                    {
                        foreach (var channel in standbyChannels)
                        {
                            if (channel != null)
                            {
                                (channel as IDisposable)?.Dispose();
                            }
                        }
                    }
                }
            }
        }

        public void ShutdownChannels()
        {
            foreach (string runtime in _workerChannels.Keys)
            {
                _logger.LogInformation("Shutting down language worker channels for runtime:{runtime}", runtime);
                if (_workerChannels.TryRemove(runtime, out List<ILanguageWorkerChannel> standbyChannels))
                {
                    foreach (var channel in standbyChannels)
                    {
                        if (channel != null)
                        {
                            (channel as IDisposable)?.Dispose();
                        }
                    }
                }
            }
        }

        internal void AddOrUpdateWorkerChannels(RpcWebHostChannelReadyEvent rpcChannelReadyEvent)
        {
            _logger.LogDebug("Adding webhost language worker channel for runtime: {language}. workerId:{id}", rpcChannelReadyEvent.Language, rpcChannelReadyEvent.LanguageWorkerChannel.Id);
            _workerChannels.AddOrUpdate(rpcChannelReadyEvent.Language,
                    (runtime) =>
                    {
                        List<ILanguageWorkerChannel> newLanguageWorkerChannels = new List<ILanguageWorkerChannel>();
                        newLanguageWorkerChannels.Add(rpcChannelReadyEvent.LanguageWorkerChannel);
                        return newLanguageWorkerChannels;
                    },
                    (runtime, existingLanguageWorkerChannels) =>
                    {
                        existingLanguageWorkerChannels.Add(rpcChannelReadyEvent.LanguageWorkerChannel);
                        return existingLanguageWorkerChannels;
                    });
        }

        public bool MatchingPlaceholderChannelExists(string selectedRuntime)
        {
            // Don't use placeholders if worker runtime not determined for function app
            if (string.IsNullOrEmpty(selectedRuntime))
            {
                return false;
            }

            // Don't use placeholders if running dotnet
            if (selectedRuntime.Equals(LanguageWorkerConstants.DotNetLanguageWorkerName, StringComparison.InvariantCultureIgnoreCase))
            {
                return false;
            }

            // Check environment config to see if placeholder worker can be used by the function app
            if (_placeholderEnvironmentConfig.TryGetValue(selectedRuntime, out Dictionary<string, string> environmentConfig))
            {
                foreach (string settingKey in environmentConfig.Keys)
                {
                    var currentValue = _environment.GetEnvironmentVariable(settingKey);
                    environmentConfig.TryGetValue(settingKey, out string cachedValue);
                    if (!string.Equals(currentValue, cachedValue))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
