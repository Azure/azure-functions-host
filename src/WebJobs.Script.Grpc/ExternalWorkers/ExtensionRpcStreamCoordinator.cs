// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Opens and maintains the runtime-to-proxy extension RPC stream for one worker channel.
/// </summary>
internal sealed partial class ExtensionRpcStreamCoordinator : IAsyncDisposable
{
    internal static readonly TimeSpan ReconnectDelay = TimeSpan.FromMilliseconds(250);

    private readonly string _workerId;
    private readonly Func<CancellationToken, AsyncDuplexStreamingCall<ExtensionRpcMessage, ExtensionRpcMessage>> _openStream;
    private readonly IExtensionRpcEndpointRouter _endpointRouter;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly TaskCompletionSource _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _activeStreamCount;
    private int _started;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionRpcStreamCoordinator"/> class.
    /// </summary>
    /// <param name="workerId">The worker identifier associated with the channel.</param>
    /// <param name="client">The extension RPC client sharing the worker channel.</param>
    /// <param name="endpointRouter">The router for registered extension endpoints.</param>
    /// <param name="logger">The logger used for stream diagnostics.</param>
    /// <param name="cancellationToken">A token that ends the coordinator lifetime.</param>
    public ExtensionRpcStreamCoordinator(
        string workerId,
        ExtensionRpc.ExtensionRpcClient client,
        IExtensionRpcEndpointRouter endpointRouter,
        ILogger logger,
        CancellationToken cancellationToken)
        : this(
            workerId,
            token => client.EventStream(cancellationToken: token),
            endpointRouter,
            logger,
            cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionRpcStreamCoordinator"/> class.
    /// </summary>
    /// <param name="workerId">The worker identifier associated with the channel.</param>
    /// <param name="openStream">The delegate that opens a physical extension RPC stream.</param>
    /// <param name="endpointRouter">The router for registered extension endpoints.</param>
    /// <param name="logger">The logger used for stream diagnostics.</param>
    /// <param name="cancellationToken">A token that ends the coordinator lifetime.</param>
    internal ExtensionRpcStreamCoordinator(
        string workerId,
        Func<CancellationToken, AsyncDuplexStreamingCall<ExtensionRpcMessage, ExtensionRpcMessage>> openStream,
        IExtensionRpcEndpointRouter endpointRouter,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        _workerId = workerId ?? throw new ArgumentNullException(nameof(workerId));
        _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
        _endpointRouter = endpointRouter ?? throw new ArgumentNullException(nameof(endpointRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    /// <summary>
    /// Gets the number of currently active physical streams.
    /// </summary>
    internal int ActiveStreamCount => Volatile.Read(ref _activeStreamCount);

    /// <summary>
    /// Runs the reconnecting extension RPC stream loop until cancellation.
    /// </summary>
    /// <returns>A task that represents the coordinator lifetime.</returns>
    public async Task RunAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) is not 0)
        {
            throw new InvalidOperationException("The extension RPC stream coordinator is already running.");
        }

        CancellationToken cancellationToken = _cancellationTokenSource.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunStreamAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (global::Grpc.Core.RpcException exception) when (
                    exception.StatusCode is StatusCode.Cancelled)
                {
                    Log.StreamDisconnected(_logger, exception, _workerId);
                }
                catch (Exception exception)
                {
                    Log.StreamFailed(_logger, exception, _workerId);
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(ReconnectDelay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _stopped.TrySetResult();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _cancellationTokenSource.CancelAsync();
        if (Volatile.Read(ref _started) is not 0)
        {
            await _stopped.Task;
        }

        _cancellationTokenSource.Dispose();
    }

    private async Task RunStreamAsync(CancellationToken cancellationToken)
    {
        using AsyncDuplexStreamingCall<ExtensionRpcMessage, ExtensionRpcMessage> call =
            _openStream(cancellationToken);
        var outbound = Channel.CreateBounded<ExtensionRpcMessage>(
            new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        await using var dispatcher = new ExtensionRpcStreamDispatcher(
            _workerId, _endpointRouter, outbound.Writer, _logger);
        using CancellationTokenSource cancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task readTask = ReadInboundAsync(call.ResponseStream, dispatcher, cancellationTokenSource.Token);
        Task writeTask = WriteOutboundAsync(call.RequestStream, outbound.Reader, cancellationTokenSource.Token);
        Volatile.Write(ref _activeStreamCount, 1);

        try
        {
            Task completedTask = await Task.WhenAny(readTask, writeTask);
            await completedTask;
        }
        finally
        {
            await cancellationTokenSource.CancelAsync();
            outbound.Writer.TryComplete();
            await Task.WhenAll(
                ObserveCompletionAsync(readTask, cancellationTokenSource.Token),
                ObserveCompletionAsync(writeTask, cancellationTokenSource.Token));
            Volatile.Write(ref _activeStreamCount, 0);
        }
    }

    private static async Task ReadInboundAsync(
        IAsyncStreamReader<ExtensionRpcMessage> responseStream,
        ExtensionRpcStreamDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        while (await responseStream.MoveNext(cancellationToken))
        {
            await dispatcher.HandleAsync(responseStream.Current, cancellationToken);
        }
    }

    private static async Task WriteOutboundAsync(
        IClientStreamWriter<ExtensionRpcMessage> requestStream,
        ChannelReader<ExtensionRpcMessage> outbound,
        CancellationToken cancellationToken)
    {
        await foreach (ExtensionRpcMessage message in outbound.ReadAllAsync(cancellationToken))
        {
            await requestStream.WriteAsync(message, cancellationToken);
        }
    }

    private async Task ObserveCompletionAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log.StreamPumpStopped(_logger, exception, _workerId);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Extension RPC stream disconnected for worker {WorkerId}; reconnecting.")]
        public static partial void StreamDisconnected(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(LogLevel.Error, "Extension RPC stream failed for worker {WorkerId}; reconnecting.")]
        public static partial void StreamFailed(ILogger logger, Exception exception, string workerId);

        [LoggerMessage(LogLevel.Debug, "Extension RPC stream pump stopped for worker {WorkerId}.")]
        public static partial void StreamPumpStopped(ILogger logger, Exception exception, string workerId);
    }
}
