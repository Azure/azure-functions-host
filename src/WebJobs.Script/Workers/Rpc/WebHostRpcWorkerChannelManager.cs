// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.AppService.Proxy.Common.Infra;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public class WebHostRpcWorkerChannelManager : IWebHostRpcWorkerChannelManager
    {
        private readonly ILogger _logger = null;
        private readonly TimeSpan workerInitTimeout = TimeSpan.FromSeconds(30);
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions = null;
        private readonly IScriptEventManager _eventManager = null;
        private readonly IEnvironment _environment;
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IRpcWorkerChannelFactory _rpcWorkerChannelFactory;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IWorkerProfileManager _profileManager;
        private string _workerRuntime;
        private Action _shutdownStandbyWorkerChannels;
        private IConfiguration _config;

        private ConcurrentDictionary<string, Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>>> _workerChannels = new ConcurrentDictionary<string, Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>>>(StringComparer.OrdinalIgnoreCase);

        public WebHostRpcWorkerChannelManager(IScriptEventManager eventManager,
                                              IEnvironment environment,
                                              ILoggerFactory loggerFactory,
                                              IRpcWorkerChannelFactory rpcWorkerChannelFactory,
                                              IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
                                              IMetricsLogger metricsLogger,
                                              IConfiguration config,
                                              IWorkerProfileManager workerProfileManager)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
            _metricsLogger = metricsLogger;
            _rpcWorkerChannelFactory = rpcWorkerChannelFactory;
            _logger = loggerFactory.CreateLogger<WebHostRpcWorkerChannelManager>();
            _applicationHostOptions = applicationHostOptions;

            _shutdownStandbyWorkerChannels = ScheduleShutdownStandbyChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(milliseconds: 5000);
        }

        public Task<IRpcWorkerChannel> InitializeChannelAsync(IEnumerable<RpcWorkerConfig> workerConfigs, string runtime)
        {
            _logger?.LogDebug("Initializing language worker channel for runtime:{runtime}", runtime);
            return InitializeLanguageWorkerChannel(workerConfigs, runtime, _applicationHostOptions.CurrentValue.ScriptPath);
        }

        internal async Task<IRpcWorkerChannel> InitializeLanguageWorkerChannel(IEnumerable<RpcWorkerConfig> workerConfigs, string runtime, string scriptRootPath)
        {
            IRpcWorkerChannel rpcWorkerChannel = null;
            string workerId = Guid.NewGuid().ToString();
            _logger.LogDebug("Creating language worker channel for runtime:{runtime}", runtime);
            try
            {
                rpcWorkerChannel = _rpcWorkerChannelFactory.Create(scriptRootPath, runtime, _metricsLogger, 0, workerConfigs);
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
                bool envReloadRequestResultSuccessful = false;
                if (UsePlaceholderChannel(rpcWorkerChannel))
                {
                    _logger.LogDebug("Loading environment variables for runtime: {runtime}", _workerRuntime);
                    envReloadRequestResultSuccessful = await rpcWorkerChannel.SendFunctionEnvironmentReloadRequest();
                }

                if (envReloadRequestResultSuccessful == false)
                {
                    _logger.LogDebug("Shutting down placeholder worker. Worker is not compatible for runtime: {runtime}", _workerRuntime);
                    // If we need to allow file edits, we should shutdown the webhost channel on specialization.
                    await ShutdownChannelIfExistsAsync(_workerRuntime, rpcWorkerChannel.Id);
                }
            }
            _shutdownStandbyWorkerChannels();
            _logger.LogDebug("Completed language worker channel specialization");
        }

        public async Task WorkerWarmupAsync()
        {
            _workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);

            if (_workerRuntime == null)
            {
                return;
            }

            IRpcWorkerChannel rpcWorkerChannel = await GetChannelAsync(_workerRuntime);
            if (rpcWorkerChannel != null)
            {
                rpcWorkerChannel.SendWorkerWarmupRequest();
            }
        }

        private bool UsePlaceholderChannel(IRpcWorkerChannel channel)
        {
            string workerRuntime = channel?.WorkerConfig?.Description?.Language;

            if (string.IsNullOrEmpty(workerRuntime))
            {
                return false;
            }

            // Restart worker process if custom languageWorkers:[runtime]:arguments are passed in
            var workerArguments = _config.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}:{workerRuntime}:{WorkerConstants.WorkerDescriptionArguments}").Value;
            if (!string.IsNullOrEmpty(workerArguments))
            {
                return false;
            }

            if (workerRuntime.Equals(RpcWorkerConstants.DotNetIsolatedLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
            {
                bool placeholderEnabled = _environment.UsePlaceholderDotNetIsolated();
                _logger.LogDebug("UsePlaceholderDotNetIsolated: {placeholderEnabled}", placeholderEnabled);

                if (!placeholderEnabled)
                {
                    return false;
                }

                // We support specialization of dotnet-isolated only on 64bit host process.
                if (!_environment.Is64BitProcess)
                {
                    _logger.LogInformation(new EventId(421, ScriptConstants.PlaceholderMissDueToBitnessEventName),
                        "This app is configured as 32-bit and therefore does not leverage all performance optimizations. See https://aka.ms/azure-functions/dotnet/placeholders for more information.");

                    return false;
                }

                // Do not specialize if the placeholder is 6.0 but the site is 7.0 (for example).
                var currentWorkerRuntimeVersion = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName);
                channel.WorkerProcess.Process.StartInfo.Environment.TryGetValue(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, out string placeholderWorkerRuntimeVersion);
                bool versionMatches = string.Equals(currentWorkerRuntimeVersion, placeholderWorkerRuntimeVersion, StringComparison.OrdinalIgnoreCase);
                _logger.LogDebug("Placeholder runtime version: '{placeholderWorkerRuntimeVersion}'. Site runtime version: '{currentWorkerRuntimeVersion}'. Match: {versionMatches}",
                    placeholderWorkerRuntimeVersion, currentWorkerRuntimeVersion, versionMatches);

                return versionMatches;
            }

            // Special case: node and PowerShell apps must be read-only to use the placeholder mode channel
            // Also cannot use placeholder worker that is targeting ~3 but has backwards compatibility with V2 enabled
            // TODO: Remove special casing when resolving https://github.com/Azure/azure-functions-host/issues/4534
            if (string.Equals(workerRuntime, RpcWorkerConstants.NodeLanguageWorkerName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(workerRuntime, RpcWorkerConstants.PowerShellLanguageWorkerName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(workerRuntime, RpcWorkerConstants.PythonLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
            {
                // Use if readonly and not v2 compatible on ~3 extension
                return _applicationHostOptions.CurrentValue.IsFileSystemReadOnly && !_environment.IsV2CompatibleOnV3Extension();
            }

            // If a profile evaluates to true and was not previously loaded, restart worker process
            if (!_profileManager.IsCorrectProfileLoaded(workerRuntime))
            {
                return false;
            }

            return true;
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
            using (_metricsLogger.LatencyEvent(MetricEventNames.SpecializationScheduleShutdownStandbyChannels))
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
        }

        public async Task ShutdownChannelsAsync()
        {
            foreach (string runtime in _workerChannels.Keys)
            {
                _logger.LogInformation("Shutting down language worker channels for runtime:{runtime}", runtime);
                if (_workerChannels.TryRemove(runtime, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> standbyChannels))
                {
                    foreach (string workerId in standbyChannels.Keys)
                    {
                        if (standbyChannels.TryGetValue(workerId, out TaskCompletionSource<IRpcWorkerChannel> channelTask))
                        {
                            IRpcWorkerChannel workerChannel = null;

                            try
                            {
                                workerChannel = await channelTask.Task;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Removing errored worker channel");
                            }

                            if (workerChannel is IDisposable disposableWorkerChannel)
                            {
                                try
                                {
                                    disposableWorkerChannel.Dispose();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogDebug(ex, "Error disposing worker channel");
                                }
                            }
                        }
                    }
                }
            }
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
