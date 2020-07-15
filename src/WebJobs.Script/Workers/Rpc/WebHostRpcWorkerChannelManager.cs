// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public class WebHostRpcWorkerChannelManager : IWebHostRpcWorkerChannelManager
    {
        private readonly ILogger _logger = null;
        private readonly TimeSpan workerInitTimeout = TimeSpan.FromSeconds(30);
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions = null;
        private readonly IOptionsMonitor<LanguageWorkerOptions> _lanuageworkerOptions = null;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IEnvironment _environment;
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IRpcWorkerChannelFactory _rpcWorkerChannelFactory;
        private readonly IMetricsLogger _metricsLogger;
        private string _workerRuntime;
        private Action _shutdownStandbyWorkerChannels;

        private ConcurrentDictionary<string, Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>>> _workerChannels = new ConcurrentDictionary<string, Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>>>(StringComparer.OrdinalIgnoreCase);

        public WebHostRpcWorkerChannelManager(IScriptEventManager eventManager, IEnvironment environment, ILoggerFactory loggerFactory, IRpcWorkerChannelFactory rpcWorkerChannelFactory, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IMetricsLogger metricsLogger, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
            _metricsLogger = metricsLogger;
            _rpcWorkerChannelFactory = rpcWorkerChannelFactory;
            _logger = loggerFactory.CreateLogger<WebHostRpcWorkerChannelManager>();
            _applicationHostOptions = applicationHostOptions;
            _lanuageworkerOptions = languageWorkerOptions;

            _shutdownStandbyWorkerChannels = ScheduleShutdownStandbyChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(milliseconds: 5000);
        }

        public Task<IRpcWorkerChannel> InitializeChannelAsync(string runtime)
        {
            _logger?.LogDebug("Initializing language worker channel for runtime:{runtime}", runtime);
            return InitializeLanguageWorkerChannel(runtime, _applicationHostOptions.CurrentValue.ScriptPath);
        }

        internal async Task<IRpcWorkerChannel> InitializeLanguageWorkerChannel(string runtime, string scriptRootPath)
        {
            IRpcWorkerChannel rpcWorkerChannel = null;
            string workerId = Guid.NewGuid().ToString();
            _logger.LogDebug("Creating language worker channel for runtime:{runtime}", runtime);
            try
            {
                rpcWorkerChannel = _rpcWorkerChannelFactory.Create(scriptRootPath, runtime, _metricsLogger, 0, _lanuageworkerOptions.CurrentValue.WorkerConfigs);
                AddOrUpdateWorkerChannels(runtime, rpcWorkerChannel);
                await rpcWorkerChannel.StartWorkerProcessAsync().ContinueWith(processStartTask =>
                {
                    if (processStartTask.Status == TaskStatus.RanToCompletion)
                    {
                        _logger.LogDebug("Adding jobhost language worker channel for runtime: {language}. workerId:{id}", _workerRuntime, rpcWorkerChannel.Id);
                        SetInitializedWorkerChannel(runtime, rpcWorkerChannel);
                    }
                    else if (processStartTask.Status == TaskStatus.Faulted)
                    {
                        _logger.LogError("Failed to start language worker process for runtime: {language}. workerId:{id}", _workerRuntime, rpcWorkerChannel.Id);
                        SetExceptionOnInitializedWorkerChannel(runtime, rpcWorkerChannel, processStartTask.Exception);
                    }
                });
            }
            catch (Exception ex)
            {
                throw new HostInitializationException($"Failed to start Language Worker Channel for language :{runtime}", ex);
            }
            return rpcWorkerChannel;
        }

        internal Task<IRpcWorkerChannel> GetChannelAsync(string language)
        {
            if (!string.IsNullOrEmpty(language) && _workerChannels.TryGetValue(language, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> workerChannels))
            {
                if (workerChannels.Count > 0 && workerChannels.TryGetValue(workerChannels.Keys.First(), out TaskCompletionSource<IRpcWorkerChannel> valueTask))
                {
                    return valueTask.Task;
                }
            }
            return Task.FromResult<IRpcWorkerChannel>(null);
        }

        public Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> GetChannels(string language)
        {
            if (!string.IsNullOrEmpty(language) && _workerChannels.TryGetValue(language, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> workerChannels))
            {
                return workerChannels;
            }
            return null;
        }

        public async Task SpecializeAsync()
        {
            _logger.LogInformation("Starting language worker channel specialization");
            _workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);

            IRpcWorkerChannel rpcWorkerChannel = await GetChannelAsync(_workerRuntime);

            if (_workerRuntime != null && rpcWorkerChannel != null)
            {
                if (UsePlaceholderChannel(_workerRuntime))
                {
                    _logger.LogDebug("Loading environment variables for runtime: {runtime}", _workerRuntime);
                    await rpcWorkerChannel.SendFunctionEnvironmentReloadRequest();
                }
                else
                {
                    _logger.LogDebug("Shutting down placeholder worker. Worker is not compatible for runtime: {runtime}", _workerRuntime);
                    // If we need to allow file edits, we should shutdown the webhost channel on specialization.
                    await ShutdownChannelIfExistsAsync(_workerRuntime, rpcWorkerChannel.Id);
                }
            }
            using (_metricsLogger.LatencyEvent(MetricEventNames.SpecializationScheduleShutdownStandbyChannels))
            {
                _shutdownStandbyWorkerChannels();
            }
            _logger.LogDebug("Completed language worker channel specialization");
        }

        private bool UsePlaceholderChannel(string workerRuntime)
        {
            if (!string.IsNullOrEmpty(workerRuntime))
            {
                // Special case: node and PowerShell apps must be read-only to use the placeholder mode channel
                // TODO: Remove special casing when resolving https://github.com/Azure/azure-functions-host/issues/4534
                if (string.Equals(workerRuntime, RpcWorkerConstants.NodeLanguageWorkerName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(workerRuntime, RpcWorkerConstants.PowerShellLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
                {
                    return _environment.IsFileSystemReadOnly();
                }
                return true;
            }
            return false;
        }

        public Task<bool> ShutdownChannelIfExistsAsync(string language, string workerId, Exception workerException = null)
        {
            if (string.IsNullOrEmpty(language))
            {
                throw new ArgumentNullException(nameof(language));
            }
            if (_workerChannels.TryRemove(language, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> rpcWorkerChannels))
            {
                if (rpcWorkerChannels.TryGetValue(workerId, out TaskCompletionSource<IRpcWorkerChannel> value))
                {
                    value?.Task.ContinueWith(channelTask =>
                    {
                        if (channelTask.Status == TaskStatus.Faulted)
                        {
                            _logger.LogDebug(channelTask.Exception, "Removing errored worker channel");
                        }
                        else
                        {
                            IRpcWorkerChannel workerChannel = channelTask.Result;
                            if (workerChannel != null)
                            {
                                _logger.LogDebug("Disposing WebHost channel for workerId: {channelId}, for runtime:{language}", workerId, language);
                                // Set exception if exists
                                workerChannel.TryFailExecutions(workerException);
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
            _workerRuntime = _workerRuntime ?? _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);
            if (!string.IsNullOrEmpty(_workerRuntime))
            {
                var standbyWorkerChannels = _workerChannels.Where(ch => !ch.Key.Equals(_workerRuntime, StringComparison.InvariantCultureIgnoreCase));
                foreach (var runtime in standbyWorkerChannels)
                {
                    using (_metricsLogger.LatencyEvent(string.Format(MetricEventNames.SpecializationShutdownStandbyChannels, runtime.Key)))
                    {
                        _logger.LogInformation("Disposing standby channel for runtime:{language}", runtime.Key);
                        if (_workerChannels.TryRemove(runtime.Key, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> standbyChannels))
                        {
                            foreach (string workerId in standbyChannels.Keys)
                            {
                                IDisposable latencyEvent = _metricsLogger.LatencyEvent(string.Format(MetricEventNames.SpecializationShutdownStandbyChannel, workerId));
                                standbyChannels[workerId]?.Task.ContinueWith(channelTask =>
                                {
                                    if (channelTask.Status == TaskStatus.Faulted)
                                    {
                                        _logger.LogDebug(channelTask.Exception, "Removing errored worker channel");
                                    }
                                    else
                                    {
                                        IRpcWorkerChannel workerChannel = channelTask.Result;
                                        if (workerChannel != null)
                                        {
                                            (channelTask.Result as IDisposable)?.Dispose();
                                        }
                                    }
                                    latencyEvent.Dispose();
                                });
                            }
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
                if (_workerChannels.TryRemove(runtime, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> standbyChannels))
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
                                IRpcWorkerChannel workerChannel = channelTask.Result;
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

        internal void AddOrUpdateWorkerChannels(string initializedRuntime, IRpcWorkerChannel initializedLanguageWorkerChannel)
        {
            _logger.LogDebug("Adding webhost language worker channel for runtime: {language}. workerId:{id}", initializedRuntime, initializedLanguageWorkerChannel.Id);
            _workerChannels.AddOrUpdate(initializedRuntime,
                    (runtime) =>
                    {
                        Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> newLanguageWorkerChannels = new Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>>();
                        newLanguageWorkerChannels.Add(initializedLanguageWorkerChannel.Id, new TaskCompletionSource<IRpcWorkerChannel>());
                        return newLanguageWorkerChannels;
                    },
                    (runtime, existingLanguageWorkerChannels) =>
                    {
                        existingLanguageWorkerChannels.Add(initializedLanguageWorkerChannel.Id, new TaskCompletionSource<IRpcWorkerChannel>());
                        return existingLanguageWorkerChannels;
                    });
        }

        internal void SetInitializedWorkerChannel(string initializedRuntime, IRpcWorkerChannel initializedLanguageWorkerChannel)
        {
            _logger.LogDebug("Adding webhost language worker channel for runtime: {language}. workerId:{id}", initializedRuntime, initializedLanguageWorkerChannel.Id);
            if (_workerChannels.TryGetValue(initializedRuntime, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> channel))
            {
                if (channel.TryGetValue(initializedLanguageWorkerChannel.Id, out TaskCompletionSource<IRpcWorkerChannel> value))
                {
                    value.SetResult(initializedLanguageWorkerChannel);
                }
            }
        }

        internal void SetExceptionOnInitializedWorkerChannel(string initializedRuntime, IRpcWorkerChannel initializedLanguageWorkerChannel, Exception exception)
        {
            _logger.LogDebug("Failed to initialize webhost language worker channel for runtime: {language}. workerId:{id}", initializedRuntime, initializedLanguageWorkerChannel.Id);
            if (_workerChannels.TryGetValue(initializedRuntime, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> channel))
            {
                if (channel.TryGetValue(initializedLanguageWorkerChannel.Id, out TaskCompletionSource<IRpcWorkerChannel> value))
                {
                    value.SetException(exception);
                }
            }
        }
    }
}
