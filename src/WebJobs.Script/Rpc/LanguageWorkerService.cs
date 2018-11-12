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
        private ILanguageWorkerChannel _languageWorkerChannel;
        private GrpcServer _rpcService;

        public LanguageWorkerService(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IOptions<LanguageWorkerOptions> languageWorkerOptions, ILoggerFactory loggerFactory, IServiceProvider rootServiceProvider)
        {
            _eventManager = (IScriptEventManager)rootServiceProvider.GetService(typeof(IScriptEventManager));
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger("Host.LanguageWorkerService.init");
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
        }

        public ILanguageWorkerChannel JavaWorkerChannel
        {
            get
            {
                if (_languageWorkerChannel == null)
                {
                    InitializeLanguageWorkerProcess().Wait();
                }
                return _languageWorkerChannel;
            }
        }

        public void Dispose()
        {
            _rpcService?.Dispose();
        }

        public async Task InitializeLanguageWorkerProcess()
        {
            _logger?.LogInformation("in InitializeLanguageWorkerProcess...");
            try
            {
                await InitializeRpcServiceAsync();
                string scriptRootPath = _applicationHostOptions.CurrentValue.ScriptPath;
                string workerId = Guid.NewGuid().ToString();
                var processFactory = new DefaultWorkerProcessFactory();
                IProcessRegistry processRegistry = ProcessRegistryFactory.Create();
                WorkerConfig javaConfig = _workerConfigs.Where(c => c.Language.Equals("java", StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();
                _logger.LogInformation("Creating languageChannelWorker...");
                _languageWorkerChannel = new LanguageWorkerChannel(_eventManager, _logger, processFactory, processRegistry, javaConfig, workerId, _rpcService);
                _logger.LogInformation($"_languageWorkerChannel null...{_languageWorkerChannel == null}");
                _languageWorkerChannel.StartWorkerProcess(scriptRootPath);
                _languageWorkerChannel.InitializeWorker();
                while (_languageWorkerChannel.InitEvent == null)
                {
                    Thread.Sleep(2000);
                }
            }
            catch (Exception grpcInitEx)
            {
                throw new HostInitializationException($"Failed to start Grpc Service. Check if your app is hitting connection limits.", grpcInitEx);
            }
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
