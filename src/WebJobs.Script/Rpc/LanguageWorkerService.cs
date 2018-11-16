// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class LanguageWorkerService : ILanguageWorkerService, IDisposable
    {
        private readonly IScriptEventManager _eventManager;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private IEnumerable<WorkerConfig> _workerConfigs;
        private Dictionary<string, ILanguageWorkerChannel> _languageWorkerChannels = new Dictionary<string, ILanguageWorkerChannel>();
        private GrpcServer _rpcService;

        public LanguageWorkerService(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IOptions<LanguageWorkerOptions> languageWorkerOptions, ILoggerFactory loggerFactory, IServiceProvider rootServiceProvider)
        {
            _eventManager = (IScriptEventManager)rootServiceProvider.GetService(typeof(IScriptEventManager));
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger("Host.LanguageWorkerService.init");
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
        }

        public IDictionary<string, ILanguageWorkerChannel> LanguageWorkerChannels
        {
            get
            {
                if (_languageWorkerChannels.Count() == 0)
                {
                    InitializeLanguageWorkerChannels().Wait();
                }
                return _languageWorkerChannels;
            }
        }

        public void Dispose()
        {
            _rpcService?.Dispose();
        }

        public async Task InitializeLanguageWorkerChannels()
        {
            _logger?.LogInformation("in InitializeLanguageWorkerProcess...");
            try
            {
                await InitializeRpcServiceAsync();
                string scriptRootPath = _applicationHostOptions.CurrentValue.ScriptPath;
                var processFactory = new DefaultWorkerProcessFactory();
                IProcessRegistry processRegistry = ProcessRegistryFactory.Create();
                foreach (WorkerConfig workerConfig in _workerConfigs)
                {
                    InitializeLanguageWorkerChannel(processFactory, processRegistry, workerConfig, scriptRootPath);
                }
            }
            catch (Exception grpcInitEx)
            {
                throw new HostInitializationException($"Failed to start Grpc Service. Check if your app is hitting connection limits.", grpcInitEx);
            }
        }

        private void InitializeLanguageWorkerChannel(IWorkerProcessFactory processFactory, IProcessRegistry processRegistry, WorkerConfig workerConfig, string scriptRootPath)
        {
            string workerId = Guid.NewGuid().ToString();
            _logger.LogInformation("Creating languageChannelWorker...");
            ILanguageWorkerChannel languageWorkerChannel = new LanguageWorkerChannel(_eventManager, _logger, processFactory, processRegistry, workerConfig, workerId, _rpcService);
            _logger.LogInformation($"_languageWorkerChannel null...{languageWorkerChannel == null}");
            languageWorkerChannel.StartWorkerProcess(scriptRootPath);
            languageWorkerChannel.InitializeWorker();
            while (languageWorkerChannel.InitEvent == null)
            {
                Thread.Sleep(2000);
            }
            _languageWorkerChannels.Add(workerConfig.Language, languageWorkerChannel);
        }

        public async Task InitializeRpcServiceAsync()
        {
            if (_rpcService == null)
            {
                try
                {
                    var serverImpl = new FunctionRpcService(_eventManager, null);
                    _rpcService = new GrpcServer(serverImpl, 30 * 1024);
                    await _rpcService.StartAsync();
                }
                catch (Exception grpcInitEx)
                {
                    throw new HostInitializationException($"Failed to start Grpc Service. Check if your app is hitting connection limits.", grpcInitEx);
                }
            }
        }
    }
}
