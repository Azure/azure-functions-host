// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
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

        private ConcurrentDictionary<string, Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>>> _workerChannels = new ConcurrentDictionary<string, Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>>>();

        public WebHostLanguageWorkerChannelManager(IScriptEventManager eventManager, IEnvironment environment, ILoggerFactory loggerFactory, ILanguageWorkerChannelFactory languageWorkerChannelFactory, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
            _languageWorkerChannelFactory = languageWorkerChannelFactory;
            _logger = loggerFactory.CreateLogger<WebHostLanguageWorkerChannelManager>();
            _applicationHostOptions = applicationHostOptions;

            _shutdownStandbyWorkerChannels = ScheduleShutdownStandbyChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(milliseconds: 5000);
        }

        public Task<ILanguageWorkerChannel> InitializeChannelAsync(string runtime)
        {
            _logger?.LogDebug("Initializing language worker channel for runtime:{runtime}", runtime);
            return InitializeLanguageWorkerChannel(runtime, _applicationHostOptions.CurrentValue.ScriptPath);
        }

        internal async Task<ILanguageWorkerChannel> InitializeLanguageWorkerChannel(string runtime, string scriptRootPath)
        {
            ILanguageWorkerChannel languageWorkerChannel = null;
            string workerId = Guid.NewGuid().ToString();
            _logger.LogDebug("Creating language worker channel for runtime:{runtime}", runtime);
            try
            {
                languageWorkerChannel = _languageWorkerChannelFactory.CreateLanguageWorkerChannel(scriptRootPath, runtime, null, 0);
                AddOrUpdateWorkerChannels(runtime, languageWorkerChannel);
                await languageWorkerChannel.StartWorkerProcessAsync().ContinueWith(processStartTask =>
                {
                    if (processStartTask.Status == TaskStatus.RanToCompletion)
                    {
                        _logger.LogDebug("Adding jobhost language worker channel for runtime: {language}. workerId:{id}", _workerRuntime, languageWorkerChannel.Id);
                        SetInitializedWorkerChannel(runtime, languageWorkerChannel);
                    }
                    else if (processStartTask.Status == TaskStatus.Faulted)
                    {
                        _logger.LogError("Failed to start language worker process for runtime: {language}. workerId:{id}", _workerRuntime, languageWorkerChannel.Id);
                        SetExceptionOnInitializedWorkerChannel(runtime, languageWorkerChannel, processStartTask.Exception);
                    }
                });
            }
            catch (Exception ex)
            {
                throw new HostInitializationException($"Failed to start Language Worker Channel for language :{runtime}", ex);
            }
            return languageWorkerChannel;
        }

        internal Task<ILanguageWorkerChannel> GetChannelAsync(string language)
        {
            if (!string.IsNullOrEmpty(language) && _workerChannels.TryGetValue(language, out Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> workerChannels))
            {
                if (workerChannels.Count > 0 && workerChannels.TryGetValue(workerChannels.Keys.First(), out TaskCompletionSource<ILanguageWorkerChannel> valueTask))
                {
                    return valueTask.Task;
                }
            }
            return Task.FromResult<ILanguageWorkerChannel>(null);
        }

        public Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> GetChannels(string language)
        {
            if (!string.IsNullOrEmpty(language) && _workerChannels.TryGetValue(language, out Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> workerChannels))
            {
                return workerChannels;
            }
            return null;
        }

        public async Task SpecializeAsync()
        {
            _logger.LogInformation("Starting language worker channel specialization");
            _workerRuntime = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName);
            ILanguageWorkerChannel languageWorkerChannel = await GetChannelAsync(_workerRuntime);
            if (_workerRuntime != null && languageWorkerChannel != null)
            {
                _logger.LogInformation("Loading environment variables for runtime: {runtime}", _workerRuntime);
                await languageWorkerChannel.SendFunctionEnvironmentReloadRequest();
            }
            _shutdownStandbyWorkerChannels();
            _logger.LogDebug("Completed language worker channel specialization");
        }

        public Task<bool> ShutdownChannelIfExistsAsync(string language, string workerId)
        {
            if (string.IsNullOrEmpty(language))
            {
                throw new ArgumentNullException(nameof(language));
            }
            if (_workerChannels.TryRemove(language, out Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> languageWorkerChannels))
            {
                if (languageWorkerChannels.TryGetValue(workerId, out TaskCompletionSource<ILanguageWorkerChannel> value))
                {
                    value?.Task.ContinueWith(channelTask =>
                    {
                        if (channelTask.Status == TaskStatus.Faulted)
                        {
                            _logger.LogDebug(channelTask.Exception, "Removing errored worker channel");
                        }
                        else
                        {
                            ILanguageWorkerChannel workerChannel = channelTask.Result;
                            if (workerChannel != null)
                            {
                                (channelTask.Result as IDisposable)?.Dispose();
                            }
                        }
                    });
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
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

                    if (_workerChannels.TryRemove(runtime.Key, out Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> standbyChannels))
                    {
                        foreach (string workerId in standbyChannels.Keys)
                        {
                            standbyChannels[workerId]?.Task.ContinueWith(channelTask =>
                            {
                                if (channelTask.Status == TaskStatus.Faulted)
                                {
                                    _logger.LogDebug(channelTask.Exception, "Removing errored worker channel");
                                }
                                else
                                {
                                    ILanguageWorkerChannel workerChannel = channelTask.Result;
                                    if (workerChannel != null)
                                    {
                                        (channelTask.Result as IDisposable)?.Dispose();
                                    }
                                }
                            });
                        }
                    }
                }
            }
        }

        public Task ShutdownChannelsAsync()
        {
            foreach (string runtime in _workerChannels.Keys)
            {
                _logger.LogInformation("Shutting down language worker channels for runtime:{runtime}", runtime);
                if (_workerChannels.TryRemove(runtime, out Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> standbyChannels))
                {
                    foreach (string workerId in standbyChannels.Keys)
                    {
                        standbyChannels[workerId]?.Task.ContinueWith(channelTask =>
                        {
                            if (channelTask.Status == TaskStatus.Faulted)
                            {
                                _logger.LogDebug(channelTask.Exception, "Removing errored worker channel");
                            }
                            else
                            {
                                ILanguageWorkerChannel workerChannel = channelTask.Result;
                                if (workerChannel != null)
                                {
                                    (channelTask.Result as IDisposable)?.Dispose();
                                }
                            }
                        });
                    }
                }
            }
            return Task.CompletedTask;
        }

        internal void AddOrUpdateWorkerChannels(string initializedRuntime, ILanguageWorkerChannel initializedLanguageWorkerChannel)
        {
            _logger.LogDebug("Adding webhost language worker channel for runtime: {language}. workerId:{id}", initializedRuntime, initializedLanguageWorkerChannel.Id);
            _workerChannels.AddOrUpdate(initializedRuntime,
                    (runtime) =>
                    {
                        Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> newLanguageWorkerChannels = new Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>>();
                        newLanguageWorkerChannels.Add(initializedLanguageWorkerChannel.Id, new TaskCompletionSource<ILanguageWorkerChannel>());
                        return newLanguageWorkerChannels;
                    },
                    (runtime, existingLanguageWorkerChannels) =>
                    {
                        existingLanguageWorkerChannels.Add(initializedLanguageWorkerChannel.Id, new TaskCompletionSource<ILanguageWorkerChannel>());
                        return existingLanguageWorkerChannels;
                    });
        }

        internal void SetInitializedWorkerChannel(string initializedRuntime, ILanguageWorkerChannel initializedLanguageWorkerChannel)
        {
            _logger.LogDebug("Adding webhost language worker channel for runtime: {language}. workerId:{id}", initializedRuntime, initializedLanguageWorkerChannel.Id);
            if (_workerChannels.TryGetValue(initializedRuntime, out Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> channel))
            {
                if (channel.TryGetValue(initializedLanguageWorkerChannel.Id, out TaskCompletionSource<ILanguageWorkerChannel> value))
                {
                    value.SetResult(initializedLanguageWorkerChannel);
                }
            }
        }

        internal void SetExceptionOnInitializedWorkerChannel(string initializedRuntime, ILanguageWorkerChannel initializedLanguageWorkerChannel, Exception exception)
        {
            _logger.LogDebug("Failed to initialize webhost language worker channel for runtime: {language}. workerId:{id}", initializedRuntime, initializedLanguageWorkerChannel.Id);
            if (_workerChannels.TryGetValue(initializedRuntime, out Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> channel))
            {
                if (channel.TryGetValue(initializedLanguageWorkerChannel.Id, out TaskCompletionSource<ILanguageWorkerChannel> value))
                {
                    value.SetException(exception);
                }
            }
        }
    }
}
