// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class RpcInitializationService : IManagedHostedService
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IEnvironment _environment;
        private readonly IWebHostLanguageWorkerChannelManager _webHostlanguageWorkerChannelManager;
        private readonly IRpcServer _rpcServer;
        private readonly ILogger _logger;

        private readonly string _workerRuntime;
        private readonly int _rpcServerShutdownTimeoutInMilliseconds;

        private Dictionary<OSPlatform, List<string>> _hostingOSToWhitelistedRuntimes = new Dictionary<OSPlatform, List<string>>()
        {
            {
                OSPlatform.Windows,
                new List<string>() { LanguageWorkerConstants.JavaLanguageWorkerName }
            },
            {
                OSPlatform.Linux,
                new List<string>() { LanguageWorkerConstants.PythonLanguageWorkerName }
            }
        };

        // _webHostLevelWhitelistedRuntimes are started at webhost level when running in Azure and locally
        private List<string> _webHostLevelWhitelistedRuntimes = new List<string>()
        {
            LanguageWorkerConstants.JavaLanguageWorkerName
        };

        public RpcInitializationService(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IEnvironment environment, IRpcServer rpcServer, IWebHostLanguageWorkerChannelManager languageWorkerChannelManager, ILogger<RpcInitializationService> logger)
        {
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
            _logger = logger;
            _rpcServer = rpcServer;
            _environment = environment;
            _rpcServerShutdownTimeoutInMilliseconds = 5000;
            _webHostlanguageWorkerChannelManager = languageWorkerChannelManager ?? throw new ArgumentNullException(nameof(languageWorkerChannelManager));
            _workerRuntime = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Utility.CheckAppOffline(_applicationHostOptions.CurrentValue.ScriptPath))
            {
                return;
            }
            _logger.LogDebug("Starting Rpc Initialization Service.");
            await InitializeRpcServerAsync();
            InitializeChannels();
            _logger.LogDebug("Rpc Initialization Service started.");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Shuttingdown Rpc Channels Manager");
            _webHostlanguageWorkerChannelManager.ShutdownChannels();
            return Task.CompletedTask;
        }

        public async Task OuterStopAsync(CancellationToken cancellationToken)
        {
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
                var hostInitEx = new HostInitializationException($"Failed to start Rpc Server. Check if your app is hitting connection limits.", grpcInitEx);
            }
        }

        internal void InitializeChannels()
        {
            if (ShouldStartInPlaceholderMode())
            {
                InitializePlaceholderChannels();
            }

            InitializeWebHostRuntimeChannels();
        }

        private void InitializePlaceholderChannels()
        {
            if (_environment.IsLinuxHostingEnvironment())
            {
                InitializePlaceholderChannels(OSPlatform.Linux);
            }

            InitializePlaceholderChannels(OSPlatform.Windows);
        }

        private void InitializePlaceholderChannels(OSPlatform os)
        {
            _hostingOSToWhitelistedRuntimes[os].Select(runtime =>
                _webHostlanguageWorkerChannelManager.CreateChannel(runtime));
        }

        private void InitializeWebHostRuntimeChannels()
        {
            if (_webHostLevelWhitelistedRuntimes.Contains(_workerRuntime))
            {
                _webHostlanguageWorkerChannelManager.CreateChannel(_workerRuntime);
            }
        }

        private bool ShouldStartInPlaceholderMode()
        {
            return string.IsNullOrEmpty(_workerRuntime) && _environment.IsPlaceholderModeEnabled();
        }

        // To help with unit tests
        internal void AddSupportedWebHostLevelRuntime(string language) => _webHostLevelWhitelistedRuntimes.Add(language);
    }
}
