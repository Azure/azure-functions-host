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
    Func<IHttpProxyService> httpProxyServiceFactory,
    IMetricsLogger metricsLogger,
    IOptionsMonitor<WorkerProcessCountOptions> processOptions = null) : IRpcClientWorkerChannelFactory
{
    internal const string WorkerOptionsName = "RpcClient";

    private readonly IAppCapabilitiesStore _appCapabilitiesStore = appCapabilitiesStore ?? throw new ArgumentNullException(nameof(appCapabilitiesStore));
    private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
    private readonly IEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IScriptEventManager _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
    private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions = hostingConfigOptions ?? throw new ArgumentNullException(nameof(hostingConfigOptions));
    private readonly IScriptHostManager _hostManager = hostManager ?? throw new ArgumentNullException(nameof(hostManager));

    private readonly IHttpProxyService _httpProxyService = httpProxyServiceFactory?.Invoke()
        ?? throw new ArgumentNullException(nameof(httpProxyServiceFactory));

    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    private readonly IMetricsLogger _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
    private readonly ISharedMemoryManager _sharedMemoryManager = sharedMemoryManager ?? throw new ArgumentNullException(nameof(sharedMemoryManager));
    private readonly IOptions<WorkerConcurrencyOptions> _workerConcurrencyOptions = workerConcurrencyOptions ?? throw new ArgumentNullException(nameof(workerConcurrencyOptions));
    private readonly WorkerProcessCountOptions _processOptions = processOptions?.Get(WorkerOptionsName) ?? new();

    internal static bool HasValidTimeouts(WorkerProcessCountOptions options) =>
        options.ProcessStartupTimeout > TimeSpan.Zero && options.InitializationTimeout > TimeSpan.Zero
        && options.EnvironmentReloadTimeout > TimeSpan.Zero
        && options.ProcessStartupTimeout.TotalMilliseconds + options.InitializationTimeout.TotalMilliseconds
            + RpcClientFactory.DefaultConnectTimeout.TotalMilliseconds <= uint.MaxValue - 1
        && options.EnvironmentReloadTimeout.TotalMilliseconds <= uint.MaxValue - 1;

    internal static TimeSpan GetLinkTimeout(WorkerProcessCountOptions options)
    {
        if (!HasValidTimeouts(options))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Worker startup and initialization require bounded positive timeouts.");
        }

        return RpcClientFactory.DefaultConnectTimeout + options.ProcessStartupTimeout + options.InitializationTimeout;
    }

    public RpcClientWorkerChannel Create(
        string workerId, DuplexChannel<StreamingMessage> ownedChannel, RuntimeWorkerCredentials credentials)
    {
        ArgumentException.ThrowIfNullOrEmpty(workerId);
        ArgumentNullException.ThrowIfNull(ownedChannel);
        ArgumentNullException.ThrowIfNull(credentials);

        RpcWorkerConfig workerConfig = new()
        {
            Description = new RpcWorkerDescription
            {
                Language = "external",
                WorkerDirectory = string.Empty,
            },
            CountOptions = new()
            {
                ProcessStartupTimeout = _processOptions.ProcessStartupTimeout,
                InitializationTimeout = _processOptions.InitializationTimeout,
                EnvironmentReloadTimeout = _processOptions.EnvironmentReloadTimeout
            },
        };
        ILogger workerLogger = _loggerFactory.CreateLogger($"Worker.LanguageWorkerChannel.{workerConfig.Description.Language}.{workerId}");
        return new(
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
            new WorkerProxyHttpProxyService(_httpProxyService, credentials),
            credentials);
    }
}
