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
                new List<string>()
                {
                    LanguageWorkerConstants.PythonLanguageWorkerName,
                    LanguageWorkerConstants.NodeLanguageWorkerName
                }
            }
        };

        // _webHostLevelWhitelistedRuntimes are started at webhost level when running in Azure and locally
        private List<string> _webHostLevelWhitelistedRuntimes = new List<string>()
        {
            LanguageWorkerConstants.JavaLanguageWorkerName
        };

        private List<string> _placeholderPoolWhitelistedRuntimes = new List<string>()
        {
            LanguageWorkerConstants.JavaLanguageWorkerName,
            LanguageWorkerConstants.NodeLanguageWorkerName
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
            await InitializeChannelsAsync();
            _logger.LogDebug("Rpc Initialization Service started.");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Shuttingdown Rpc Channels Manager");
            await _webHostlanguageWorkerChannelManager.ShutdownChannelsAsync();
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

        internal Task InitializeChannelsAsync()
        {
            // TODO: Remove special casing when resolving https://github.com/Azure/azure-functions-host/issues/4534
            if (ShouldStartAsPlaceholderPool())
            {
                return _webHostlanguageWorkerChannelManager.InitializeChannelAsync(_workerRuntime);
            }
            else if (ShouldStartStandbyPlaceholderChannels())
            {
                return InitializePlaceholderChannelsAsync();
            }

            return InitializeWebHostRuntimeChannelsAsync();
        }

        private Task InitializePlaceholderChannelsAsync()
        {
            if (_environment.IsLinuxHostingEnvironment())
            {
                return InitializePlaceholderChannelsAsync(OSPlatform.Linux);
            }

            return InitializePlaceholderChannelsAsync(OSPlatform.Windows);
        }

        private Task InitializePlaceholderChannelsAsync(OSPlatform os)
        {
            return Task.WhenAll(_hostingOSToWhitelistedRuntimes[os].Select(runtime =>
                _webHostlanguageWorkerChannelManager.InitializeChannelAsync(runtime)));
        }

        private Task InitializeWebHostRuntimeChannelsAsync()
        {
            if (_webHostLevelWhitelistedRuntimes.Contains(_workerRuntime, StringComparer.OrdinalIgnoreCase))
            {
                return _webHostlanguageWorkerChannelManager.InitializeChannelAsync(_workerRuntime);
            }

            return Task.CompletedTask;
        }

        internal bool ShouldStartStandbyPlaceholderChannels()
        {
            if (string.IsNullOrEmpty(_workerRuntime) && _environment.IsPlaceholderModeEnabled())
            {
                if (_environment.IsLinuxHostingEnvironment())
                {
                    return true;
                }
                // On Windows AppService Env, only start worker processes for legacy template site: FunctionsPlaceholderTemplateSite
                return _environment.IsLegacyPlaceholderTemplateSite();
            }
            return false;
        }

        internal bool ShouldStartAsPlaceholderPool()
        {
            // We are in placeholder mode but a worker runtime IS set
            return _environment.IsPlaceholderModeEnabled()
                && !string.IsNullOrEmpty(_workerRuntime)
                && _placeholderPoolWhitelistedRuntimes.Contains(_workerRuntime, StringComparer.OrdinalIgnoreCase);
        }

        // To help with unit tests
        internal void AddSupportedWebHostLevelRuntime(string language) => _webHostLevelWhitelistedRuntimes.Add(language);
    }
}
