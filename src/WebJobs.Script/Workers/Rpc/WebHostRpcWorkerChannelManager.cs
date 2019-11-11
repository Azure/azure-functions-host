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
        private readonly IScriptEventManager _eventManager = null;
        private readonly IEnvironment _environment;
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly IRpcWorkerChannelFactory _rpcWorkerChannelFactory;
        private readonly IMetricsLogger _metricsLogger;
        private string _workerRuntime;
        private Action _shutdownStandbyWorkerChannels;

        private ConcurrentDictionary<string, Dictionary<string, IRpcWorkerChannel>> _workerChannels = new ConcurrentDictionary<string, Dictionary<string, IRpcWorkerChannel>>(StringComparer.OrdinalIgnoreCase);

        public WebHostRpcWorkerChannelManager(IScriptEventManager eventManager, IEnvironment environment, ILoggerFactory loggerFactory, IRpcWorkerChannelFactory rpcWorkerChannelFactory, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IMetricsLogger metricsLogger)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _eventManager = eventManager;
            _loggerFactory = loggerFactory;
            _metricsLogger = metricsLogger;
            _rpcWorkerChannelFactory = rpcWorkerChannelFactory;
            _logger = loggerFactory.CreateLogger<WebHostRpcWorkerChannelManager>();
            _applicationHostOptions = applicationHostOptions;

            _shutdownStandbyWorkerChannels = ScheduleShutdownStandbyChannels;
            _shutdownStandbyWorkerChannels = _shutdownStandbyWorkerChannels.Debounce(milliseconds: 5000);
        }

        public IRpcWorkerChannel InitializeChannel(string runtime)
        {
            _logger?.LogDebug("Initializing language worker channel for runtime:{runtime}", runtime);
            return InitializeLanguageWorkerChannel(runtime, _applicationHostOptions.CurrentValue.ScriptPath);
        }

        internal IRpcWorkerChannel InitializeLanguageWorkerChannel(string runtime, string scriptRootPath)
        {
            IRpcWorkerChannel rpcWorkerChannel = null;
            string workerId = Guid.NewGuid().ToString();
            _logger.LogDebug("Creating language worker channel for runtime:{runtime}", runtime);
            try
            {
                rpcWorkerChannel = _rpcWorkerChannelFactory.Create(scriptRootPath, runtime, _metricsLogger, 0);
                AddOrUpdateWorkerChannels(runtime, rpcWorkerChannel);
                rpcWorkerChannel.StartWorkerProcess();
            }
            catch (Exception ex)
            {
                throw new HostInitializationException($"Failed to start Language Worker Channel for language :{runtime}", ex);
            }
            return rpcWorkerChannel;
        }

        internal IRpcWorkerChannel GetChannel(string language)
        {
            if (!string.IsNullOrEmpty(language) && _workerChannels.TryGetValue(language, out Dictionary<string, IRpcWorkerChannel> workerChannels))
            {
                if (workerChannels.Count > 0 && workerChannels.TryGetValue(workerChannels.Keys.First(), out IRpcWorkerChannel channel))
                {
                    return channel;
                }
            }
            return null;
        }

        public Dictionary<string, IRpcWorkerChannel> GetChannels(string language)
        {
            if (!string.IsNullOrEmpty(language) && _workerChannels.TryGetValue(language, out Dictionary<string, IRpcWorkerChannel> workerChannels))
            {
                return workerChannels;
            }
            return null;
        }

        public void Specialize()
        {
            _logger.LogInformation("Starting language worker channel specialization");
            _workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);

            IRpcWorkerChannel rpcWorkerChannel = GetChannel(_workerRuntime);

            if (_workerRuntime != null && rpcWorkerChannel != null)
            {
                if (UsePlaceholderChannel(_workerRuntime))
                {
                    _logger.LogDebug("Loading environment variables for runtime: {runtime}", _workerRuntime);
                    rpcWorkerChannel.SendFunctionEnvironmentReloadRequest();
                }
                else
                {
                    _logger.LogDebug("Shutting down placeholder worker. Worker is not compatible for runtime: {runtime}", _workerRuntime);
                    // If we need to allow file edits, we should shutdown the webhost channel on specialization.
                    ShutdownChannelIfExists(_workerRuntime, rpcWorkerChannel.Id);
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
                // Special case: node apps must be read-only to use the placeholder mode channel
                // TODO: Remove special casing when resolving https://github.com/Azure/azure-functions-host/issues/4534
                if (string.Equals(workerRuntime, RpcWorkerConstants.NodeLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
                {
                    return _environment.IsFileSystemReadOnly();
                }
                return true;
            }
            return false;
        }

        public bool ShutdownChannelIfExists(string language, string workerId)
        {
            if (string.IsNullOrEmpty(language))
            {
                throw new ArgumentNullException(nameof(language));
            }
            if (_workerChannels.TryRemove(language, out Dictionary<string, IRpcWorkerChannel> rpcWorkerChannels))
            {
                if (rpcWorkerChannels.TryGetValue(workerId, out IRpcWorkerChannel channel))
                {
                    (channel as IDisposable)?.Dispose();
                    return true;
                }
            }
            return false;
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
                        if (_workerChannels.TryRemove(runtime.Key, out Dictionary<string, IRpcWorkerChannel> standbyChannels))
                        {
                            foreach (string workerId in standbyChannels.Keys)
                            {
                                IDisposable latencyEvent = _metricsLogger.LatencyEvent(string.Format(MetricEventNames.SpecializationShutdownStandbyChannel, workerId));
                                var channel = standbyChannels[workerId];
                                if (channel != null)
                                {
                                    (channel as IDisposable)?.Dispose();
                                }
                                latencyEvent.Dispose();
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
                if (_workerChannels.TryRemove(runtime, out Dictionary<string, IRpcWorkerChannel> standbyChannels))
                {
                    foreach (string workerId in standbyChannels.Keys)
                    {
                        var channel = standbyChannels[workerId];
                        if (channel != null)
                        {
                            (channel as IDisposable)?.Dispose();
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
                        Dictionary<string, IRpcWorkerChannel> newLanguageWorkerChannels = new Dictionary<string, IRpcWorkerChannel>();
                        newLanguageWorkerChannels.Add(initializedLanguageWorkerChannel.Id, initializedLanguageWorkerChannel);
                        return newLanguageWorkerChannels;
                    },
                    (runtime, existingLanguageWorkerChannels) =>
                    {
                        existingLanguageWorkerChannels.Add(initializedLanguageWorkerChannel.Id, initializedLanguageWorkerChannel);
                        return existingLanguageWorkerChannels;
                    });
        }
    }
}
