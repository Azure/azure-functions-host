// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Retrieves and validates function metadata from a client-backed worker channel.
/// </summary>
/// <remarks>
/// One provider belongs to a ScriptHost child container so its metadata cache is replaced on host restart. The provider
/// borrows the root-owned registry and requests registry cleanup when a metadata operation makes a channel unusable.
/// </remarks>
internal sealed partial class RpcClientWorkerFunctionMetadataProvider : IWorkerFunctionMetadataProvider
{
    private const string MetadataProviderName = "RpcClient";
    private static readonly TimeSpan DefaultChannelWaitTimeout = TimeSpan.FromSeconds(10);

    private readonly IWorkerChannelRegistry _channelRegistry;
    private readonly TimeSpan _channelWaitTimeout;
    private readonly ILogger<RpcClientWorkerFunctionMetadataProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly WorkerFunctionMetadataValidator _validator;
    private FunctionMetadataResult _cachedResult;

    public RpcClientWorkerFunctionMetadataProvider(
        IWorkerChannelRegistry channelRegistry,
        ILogger<RpcClientWorkerFunctionMetadataProvider> logger,
        IWorkerRuntimeResolver workerRuntimeResolver)
        : this(channelRegistry, logger, workerRuntimeResolver, DefaultChannelWaitTimeout)
    {
    }

    internal RpcClientWorkerFunctionMetadataProvider(
        IWorkerChannelRegistry channelRegistry,
        ILogger<RpcClientWorkerFunctionMetadataProvider> logger,
        IWorkerRuntimeResolver workerRuntimeResolver,
        TimeSpan channelWaitTimeout)
    {
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = new(logger, workerRuntimeResolver ?? throw new ArgumentNullException(nameof(workerRuntimeResolver)));

        if (channelWaitTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(channelWaitTimeout), "The channel wait timeout must be greater than zero.");
        }

        _channelWaitTimeout = channelWaitTimeout;
    }

    public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors => _validator.FunctionErrors;

    public async Task<FunctionMetadataResult> GetFunctionMetadataAsync(
        IEnumerable<RpcWorkerConfig> workerConfigs,
        bool forceRefresh = false)
    {
        using SemaphoreLock refreshLock = await _refreshLock.LockAsync();
        if (!forceRefresh && _cachedResult is not null)
        {
            return _cachedResult;
        }

        Log.ReadingMetadata(_logger, MetadataProviderName);
        WorkerChannel channel = await GetInitializedChannelAsync();
        Log.FetchingMetadata(_logger, channel.Id);
        List<RawFunctionMetadata> rawFunctions;
        try
        {
            rawFunctions = await channel.GetFunctionMetadata().WaitAsync(_channelWaitTimeout);
        }
        catch (Exception exception)
        {
            try
            {
                await _channelRegistry.UnlinkAsync(channel.Id);
            }
            catch (Exception unlinkException)
            {
                throw new AggregateException("Function metadata failed and the worker channel could not be unlinked.",
                    exception, unlinkException);
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }

        if (rawFunctions.Any(metadata => metadata.UseDefaultMetadataIndexing))
        {
            throw new InvalidOperationException(
                "Client-backed workers must provide function metadata and cannot request host metadata indexing.");
        }

        ImmutableArray<FunctionMetadata> functions = _validator.ValidateMetadata(rawFunctions);
        Log.FunctionsReturned(_logger, functions.Length, MetadataProviderName);
        _cachedResult = new(useDefaultMetadataIndexing: false, functions);
        return _cachedResult;
    }

    private async Task<WorkerChannel> GetInitializedChannelAsync()
    {
        WorkerChannel channel = _channelRegistry.GetInitializedChannels()
            .OrderBy(channel => channel.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (channel is not null)
        {
            return channel;
        }

        using CancellationTokenSource timeoutSource = new(_channelWaitTimeout);
        try
        {
            return await _channelRegistry.WaitForFirstInitializedAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException($"No client-backed worker channel initialized within {_channelWaitTimeout}.");
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Debug, "Fetching function metadata from client-backed worker {WorkerId}.")]
        public static partial void FetchingMetadata(ILogger logger, string workerId);

        [LoggerMessage(1, LogLevel.Information, "Reading functions metadata ({Provider}).")]
        public static partial void ReadingMetadata(ILogger logger, string provider);

        [LoggerMessage(2, LogLevel.Information, "{Count} functions returned by metadata provider {Provider}.")]
        public static partial void FunctionsReturned(ILogger logger, int count, string provider);
    }
}
