// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public class RpcInitializationService : IManagedHostedService
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IEnvironment _environment;
        private readonly IWebHostRpcWorkerChannelManager _webHostRpcWorkerChannelManager;
        private readonly IRpcServer _rpcServer;
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<LanguageWorkerOptions> _languageWorkerOptions;

        private readonly string _workerRuntime;
        private readonly int _rpcServerShutdownTimeoutInMilliseconds;
        private HashSet<string> _placeholderLanguageWorkersList = new HashSet<string>();

        public RpcInitializationService(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IEnvironment environment, IRpcServer rpcServer,
            IWebHostRpcWorkerChannelManager rpcWorkerChannelManager, ILogger<RpcInitializationService> logger, IOptionsMonitor<LanguageWorkerOptions> languageWorkerOptions)
        {
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
            _logger = logger;
            _rpcServer = rpcServer;
            _environment = environment;
            _rpcServerShutdownTimeoutInMilliseconds = 5000;
            _webHostRpcWorkerChannelManager = rpcWorkerChannelManager ?? throw new ArgumentNullException(nameof(rpcWorkerChannelManager));
            _workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);
            _placeholderLanguageWorkersList = _environment.GetLanguageWorkerListToStartInPlaceholder();
            _languageWorkerOptions = languageWorkerOptions;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Utility.CheckAppOffline(_applicationHostOptions.CurrentValue.ScriptPath))
            {
                _logger.LogDebug("App is offline. RpcInitializationService will not be started");
                return;
            }

            // TODO: https://github.com/Azure/azure-functions-host/issues/4891
            try
            {
                _logger.LogDebug("Starting Rpc Initialization Service.");
                await InitializeRpcServerAsync();
                await InitializeChannelsAsync();
                _logger.LogDebug("Rpc Initialization Service started.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting Rpc Initialization Service. Handling error and continuing.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task OuterStopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Shutting down Rpc Channels Manager");
            await _webHostRpcWorkerChannelManager.ShutdownChannelsAsync();

            _logger.LogDebug("Shutting down RPC server");

            try
            {
                Task shutDownRpcServer = _rpcServer.ShutdownAsync();
                Task shutdownResult = await Task.WhenAny(shutDownRpcServer, Task.Delay(_rpcServerShutdownTimeoutInMilliseconds));

                if (!shutdownResult.Equals(shutDownRpcServer) || shutDownRpcServer.IsFaulted)
                {
                    _logger.LogDebug("Killing RPC server");
                    await _rpcServer.KillAsync();
                }
            }
            catch (AggregateException ae)
            {
                ae.Handle(e =>
                {
                    _logger.LogError(e, "Shutting down RPC server encountered exception: '{message}'", e.Message);
                    return true;
                });
            }
        }

        internal async Task InitializeRpcServerAsync()
        {
            try
            {
                _logger.LogDebug("Initializing RpcServer");
                await _rpcServer.StartAsync();
                _logger.LogDebug("RpcServer initialized");
            }
            catch (Exception grpcInitEx)
            {
                throw new HostInitializationException($"Failed to start Rpc Server. Check if your app is hitting connection limits.", grpcInitEx);
            }
        }

        internal Task InitializeChannelsAsync()
        {
            if (_placeholderLanguageWorkersList == null)
            {
                throw new ArgumentNullException(nameof(_placeholderLanguageWorkersList));
            }

            if (_environment.IsPlaceholderModeEnabled())
            {
                _logger.LogDebug("Initializing language worker channels. {workerRuntimeSetting}: '{workerRuntime}', placeholderChannelList: '{placeholderChannelList}' in placeholder mode.", nameof(RpcWorkerConstants.FunctionWorkerRuntimeSettingName), _workerRuntime, string.Join(",", _placeholderLanguageWorkersList));

                if (_placeholderLanguageWorkersList.Count() != 0)
                {
                    return Task.WhenAll(_placeholderLanguageWorkersList.Select(runtime =>
                    _webHostRpcWorkerChannelManager.InitializeChannelAsync(_languageWorkerOptions.CurrentValue.WorkerConfigs, runtime)));
                }
            }
            return Task.CompletedTask;
        }
    }
}
