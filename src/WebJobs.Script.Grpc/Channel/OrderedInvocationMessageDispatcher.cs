// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

internal sealed class OrderedInvocationMessageDispatcher : IInvocationMessageDispatcher, IDisposable
{
    private readonly ILogger _logger;
    private readonly string _invocationId;
    private readonly Action<InboundGrpcEvent> _dispatchWithChannel;

    // Separated these out for easier testing of the flow.
    private readonly Action<InboundGrpcEvent> _dispatchWithThreadPool;

    private Channel<InboundGrpcEvent> _channel;
    private bool _isChannelInitialized = false;
    private bool _invocationComplete = false;
    private bool _disposed = false;

    public OrderedInvocationMessageDispatcher(string invocationId, ILogger logger, Action<InboundGrpcEvent> processItem)
        : this(invocationId, logger, processItem, processItem)
    {
    }

    // For testing
    internal OrderedInvocationMessageDispatcher(string invocationId, ILogger logger, Action<InboundGrpcEvent> processItemWithChannel,
        Action<InboundGrpcEvent> processItemWithThreadPool)
    {
        _logger = logger;
        _invocationId = invocationId;
        _dispatchWithChannel = processItemWithChannel;
        _dispatchWithThreadPool = processItemWithThreadPool;
    }

    // For testing
    internal Channel<InboundGrpcEvent> MessageChannel => _channel;

    private static Channel<InboundGrpcEvent> InitializeChannel() =>
        Channel.CreateUnbounded<InboundGrpcEvent>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

    public void DispatchRpcLog(InboundGrpcEvent msg)
    {
        // The channel is only needed if we use any logs. Otherwise, ordering doesn't matter.
        // This is not thread-safe, but it is always called in-order from the same thread, so no
        // need for locking.
        // We also do not want to initialize the channel if we're receiving an out-of-order RpcLog and
        // the invocation is already done.
        if (!_isChannelInitialized && !_invocationComplete)
        {
            _channel = InitializeChannel();
            _ = ReadMessagesAsync();
            _isChannelInitialized = true;
        }

        WriteToChannel(msg);
    }

    public void DispatchInvocationResponse(InboundGrpcEvent msg)
    {
        // Any other messages that come here shouldn't use the Channel.
        _invocationComplete = true;

        if (_isChannelInitialized)
        {
            WriteToChannel(msg);

            // This signals that we're done with this invocation.
            _channel.Writer.TryComplete();
        }
        else
        {
            // Channel was never started. We must not have needed it. Send directly to ThreadPool.
            DispatchToThreadPool(msg);
        }
    }

    private void WriteToChannel(InboundGrpcEvent msg)
    {
        if (_channel is null || !_channel.Writer.TryWrite(msg))
        {
            // If this fails, fall back to the ThreadPool
            _logger.LogDebug("Cannot write '{msgType}' to channel for InvocationId '{functionInvocationId}'. Dispatching message to the ThreadPool.", msg.MessageType, _invocationId);
            DispatchToThreadPool(msg);
        }
    }

    private void DispatchToThreadPool(InboundGrpcEvent msg) =>
        ThreadPool.QueueUserWorkItem(state => _dispatchWithThreadPool((InboundGrpcEvent)state), msg);

    private async Task ReadMessagesAsync()
    {
        try
        {
            await foreach (InboundGrpcEvent msg in _channel.Reader.ReadAllAsync())
            {
                // Assume the Action being called is already wrapped in a try/catch
                _dispatchWithChannel(msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while reading InvocationProcessor channel for InvocationId '{functionInvocationId}'.", _invocationId);

            // Ensure nothing else will be written to the channel. There is a possibility
            // that some messages are lost here.
            _channel.Writer.TryComplete(ex);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _channel?.Writer.TryComplete();
        }
    }
}