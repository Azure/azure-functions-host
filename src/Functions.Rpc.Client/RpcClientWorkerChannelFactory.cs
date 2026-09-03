// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates client-backed worker channels with the shared protocol dependencies.
/// </summary>
internal sealed class RpcClientWorkerChannelFactory(
    IScriptEventManager eventManager,
    IScriptHostManager hostManager,
    IEnvironment environment,
    ILoggerFactory loggerFactory,
    IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
    ISharedMemoryManager sharedMemoryManager,
    IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions,
    IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
    IAppCapabilitiesStore appCapabilitiesStore,
    IHttpProxyService httpProxyService,
    IMetricsLogger metricsLogger) : IRpcClientWorkerChannelFactory
{
    private readonly IAppCapabilitiesStore _appCapabilitiesStore = appCapabilitiesStore ?? throw new ArgumentNullException(nameof(appCapabilitiesStore));
    private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
    private readonly IEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IScriptEventManager _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
    private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions = hostingConfigOptions ?? throw new ArgumentNullException(nameof(hostingConfigOptions));
    private readonly IScriptHostManager _hostManager = hostManager ?? throw new ArgumentNullException(nameof(hostManager));
    private readonly IHttpProxyService _httpProxyService = httpProxyService ?? throw new ArgumentNullException(nameof(httpProxyService));
    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    private readonly IMetricsLogger _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
    private readonly ISharedMemoryManager _sharedMemoryManager = sharedMemoryManager ?? throw new ArgumentNullException(nameof(sharedMemoryManager));
    private readonly IOptions<WorkerConcurrencyOptions> _workerConcurrencyOptions = workerConcurrencyOptions ?? throw new ArgumentNullException(nameof(workerConcurrencyOptions));

    public RpcClientWorkerChannel Create(string workerId, DuplexChannel<StreamingMessage> ownedChannel)
    {
        ArgumentException.ThrowIfNullOrEmpty(workerId);
        ArgumentNullException.ThrowIfNull(ownedChannel);

        RpcWorkerConfig workerConfig = new()
        {
            Description = new RpcWorkerDescription
            {
                Language = "external",
                WorkerDirectory = string.Empty,
            },
            CountOptions = new WorkerProcessCountOptions(),
        };
        ILogger workerLogger = _loggerFactory.CreateLogger($"Worker.LanguageWorkerChannel.{workerConfig.Description.Language}.{workerId}");
        return new RpcClientWorkerChannel(
            workerId,
            ownedChannel,
            _eventManager,
            _hostManager,
            workerConfig,
            workerLogger,
            _metricsLogger,
            attemptCount: 0,
            _environment,
            _applicationHostOptions,
            _sharedMemoryManager,
            _workerConcurrencyOptions,
            _hostingConfigOptions,
            _appCapabilitiesStore,
            _httpProxyService);
    }
}
