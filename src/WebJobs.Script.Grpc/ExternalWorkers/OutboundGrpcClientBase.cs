// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Provides the shared connection lifecycle for outbound gRPC relay clients.
/// </summary>
internal abstract partial class OutboundGrpcClientBase : IOutboundGrpcClient
{
    internal static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan DefaultKeepAlivePingDelay = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan DefaultKeepAlivePingTimeout = TimeSpan.FromSeconds(10);

    // The runtime-to-worker link is a single host-local pair with no load
    // concerns, so a short constant retry bounds proxy discovery latency.
    internal static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromMilliseconds(25);

    private readonly IScriptEventManager _eventManager;
    private readonly Func<Uri, GrpcChannel> _channelFactory;
    private GrpcChannel? _channel;
    private IDisposable? _call;
    private CancellationTokenSource? _cts;

    protected OutboundGrpcClientBase(
        IScriptEventManager eventManager,
        ILogger logger,
        Func<Uri, GrpcChannel> channelFactory)
    {
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
    }

    protected ILogger Logger { get; }

    /// <inheritdoc/>
    public Task? InboundPumpTask { get; private set; }

    /// <inheritdoc/>
    public async Task ConnectAsync(string workerId, Uri endpoint, CancellationToken cancellationToken)
    {
        var connectStart = Stopwatch.GetTimestamp();
        Log.ConnectStarted(Logger, workerId, endpoint);

        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _channel = _channelFactory(endpoint);

            using var readinessCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            readinessCts.CancelAfter(DefaultReadyTimeout);

            var readinessStart = Stopwatch.GetTimestamp();
            await _channel.ConnectAsync(readinessCts.Token);
            Log.ChannelConnected(
                Logger,
                workerId,
                endpoint,
                Stopwatch.GetElapsedTime(readinessStart).TotalMilliseconds,
                Stopwatch.GetElapsedTime(connectStart).TotalMilliseconds);

            if (!_eventManager.TryGetGrpcChannels(workerId, out var inbound, out var outbound))
            {
                throw new InvalidOperationException($"No pre-registered gRPC channels found for worker '{workerId}'.");
            }

            OutboundGrpcStreamConnection stream = OpenStream(workerId, _channel, inbound, outbound, _cts.Token);
            _call = stream.Call;
            InboundPumpTask = stream.InboundPumpTask;

            Log.StreamEstablished(
                Logger,
                workerId,
                endpoint,
                Stopwatch.GetElapsedTime(connectStart).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            Log.ConnectFailed(Logger, ex, workerId, endpoint, Stopwatch.GetElapsedTime(connectStart).TotalMilliseconds);

            Interlocked.Exchange(ref _call, null)?.Dispose();
            Interlocked.Exchange(ref _channel, null)?.Dispose();
            Interlocked.Exchange(ref _cts, null)?.Dispose();
            throw;
        }
    }

    protected abstract OutboundGrpcStreamConnection OpenStream(
        string workerId,
        GrpcChannel channel,
        System.Threading.Channels.Channel<InboundGrpcEvent> inbound,
        System.Threading.Channels.Channel<OutboundGrpcEvent> outbound,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates an outbound gRPC channel for the specified proxy endpoint.
    /// </summary>
    /// <param name="endpoint">The proxy endpoint.</param>
    /// <returns>The configured gRPC channel.</returns>
    internal static GrpcChannel CreateGrpcChannel(Uri endpoint)
    {
        return GrpcChannel.ForAddress(endpoint, CreateGrpcChannelOptions());
    }

    /// <summary>
    /// Creates the shared outbound gRPC channel options.
    /// </summary>
    /// <returns>The configured channel options.</returns>
    internal static GrpcChannelOptions CreateGrpcChannelOptions()
    {
        return new GrpcChannelOptions
        {
            HttpHandler = CreateHttpHandler(),
            ServiceProvider = OutboundGrpcBackoffPolicyServiceProvider.Instance
        };
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        Interlocked.Exchange(ref _call, null)?.Dispose();
        Interlocked.Exchange(ref _channel, null)?.Dispose();
    }

    private static SocketsHttpHandler CreateHttpHandler()
    {
        return new SocketsHttpHandler
        {
            ConnectTimeout = DefaultConnectTimeout,
            KeepAlivePingDelay = DefaultKeepAlivePingDelay,
            KeepAlivePingTimeout = DefaultKeepAlivePingTimeout,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
        };
    }

    private static partial class Log
    {
        [LoggerMessage(
            LogLevel.Information,
            "OutboundGrpcClient connect started. WorkerId: {workerId}, Endpoint: {endpoint}.")]
        public static partial void ConnectStarted(ILogger logger, string workerId, Uri endpoint);

        [LoggerMessage(
            LogLevel.Information,
            "OutboundGrpcClient channel connected. WorkerId: {workerId}, Endpoint: {endpoint}, "
            + "StepElapsedMilliseconds: {stepElapsedMilliseconds}, ElapsedMilliseconds: {elapsedMilliseconds}.")]
        public static partial void ChannelConnected(
            ILogger logger,
            string workerId,
            Uri endpoint,
            double stepElapsedMilliseconds,
            double elapsedMilliseconds);

        [LoggerMessage(
            LogLevel.Information,
            "OutboundGrpcClient stream established. WorkerId: {workerId}, Endpoint: {endpoint}, "
            + "ElapsedMilliseconds: {elapsedMilliseconds}.")]
        public static partial void StreamEstablished(
            ILogger logger,
            string workerId,
            Uri endpoint,
            double elapsedMilliseconds);

        [LoggerMessage(
            LogLevel.Warning,
            "OutboundGrpcClient connect failed. WorkerId: {workerId}, Endpoint: {endpoint}, "
            + "ElapsedMilliseconds: {elapsedMilliseconds}.")]
        public static partial void ConnectFailed(
            ILogger logger,
            Exception exception,
            string workerId,
            Uri endpoint,
            double elapsedMilliseconds);
    }
}
