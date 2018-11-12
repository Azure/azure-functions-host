// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class RpcInitializationService : IHostedService
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;

        private readonly ILanguageWorkerChannelManager _languageWorkerChannelManager;
        private readonly IRpcServer _rpcServer;
        private readonly ILogger _logger;

        public RpcInitializationService(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IRpcServer rpcServer, ILanguageWorkerChannelManager languageWorkerChannelManager, ILoggerFactory loggerFactory)
        {
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryRpcInitializationService);
            _rpcServer = rpcServer;
            _languageWorkerChannelManager = languageWorkerChannelManager ?? throw new ArgumentNullException(nameof(languageWorkerChannelManager));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Utility.CheckAppOffline(_applicationHostOptions.CurrentValue.ScriptPath))
            {
                return;
            }
            _logger.LogInformation("Initializing Rpc Channels Manager");
            await InitializeRpcServerAsync();
            await _languageWorkerChannelManager.InitializeAsync();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Shuttingdown Rpc Channels Manager");
            await _rpcServer.KillAsync();
            await _languageWorkerChannelManager.ShutdownChannelsAsync();
        }

        private async Task InitializeRpcServerAsync()
        {
            try
            {
                _logger.LogInformation("Initializaing RpcServer");
                await _rpcServer.StartAsync();
            }
            catch (Exception grpcInitEx)
            {
                var hostInitEx = new HostInitializationException($"Failed to start Rpc Server. Check if your app is hitting connection limits.", grpcInitEx);
            }
        }
    }
}
