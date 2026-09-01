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
internal sealed class RpcClientWorkerChannelFactory(IScriptEventManager eventManager, IScriptHostManager hostManager, IEnvironment environment,
    ILoggerFactory loggerFactory, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, ISharedMemoryManager sharedMemoryManager,
    IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions, IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
    IAppCapabilitiesStore appCapabilitiesStore, IHttpProxyService httpProxyService)
{
    public RpcClientWorkerChannel Create(string workerId, DuplexChannel<StreamingMessage> ownedChannel, RpcWorkerConfig workerConfig,
        IMetricsLogger metricsLogger, int attemptCount)
    {
        ArgumentException.ThrowIfNullOrEmpty(workerId);
        ArgumentNullException.ThrowIfNull(ownedChannel);
        ArgumentNullException.ThrowIfNull(workerConfig);
        ArgumentNullException.ThrowIfNull(metricsLogger);
        ArgumentNullException.ThrowIfNull(eventManager);
        ArgumentNullException.ThrowIfNull(hostManager);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(applicationHostOptions);
        ArgumentNullException.ThrowIfNull(sharedMemoryManager);
        ArgumentNullException.ThrowIfNull(workerConcurrencyOptions);
        ArgumentNullException.ThrowIfNull(hostingConfigOptions);
        ArgumentNullException.ThrowIfNull(appCapabilitiesStore);
        ArgumentNullException.ThrowIfNull(httpProxyService);

        ILogger workerLogger = loggerFactory.CreateLogger($"Worker.LanguageWorkerChannel.{workerConfig.Description.Language}.{workerId}");
        return new RpcClientWorkerChannel(workerId, ownedChannel, workerConfig.CountOptions.ProcessStartupTimeout, eventManager, hostManager,
            workerConfig, workerLogger, metricsLogger, attemptCount, environment, applicationHostOptions, sharedMemoryManager,
            workerConcurrencyOptions, hostingConfigOptions, appCapabilitiesStore, httpProxyService);
    }
}
