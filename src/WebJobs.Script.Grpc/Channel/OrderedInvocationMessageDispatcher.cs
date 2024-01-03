// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

/// <summary>
/// An implementation of <see cref="IInvocationMessageDispatcher"/> that internally uses a per-invocation <see cref="Channel{T}"/> to ensure
/// ordering of messages is maintained. The calls to <see cref="DispatchRpcLog(InboundGrpcEvent)"/> and <see cref="DispatchInvocationResponse(InboundGrpcEvent)"/>
/// both write messages to the same <see cref="Channel{T}"/>. Then, a background <see cref="Task"/> reads messages from this channel and invokes the
/// <see cref="Action"/> provided in the constructor.
///
/// This dispatcher is created with the <see cref="OrderedInvocationMessageDispatcherFactory"/> to ensure that instances are created and disposed
/// per-invocation. This means that every instance is only ever responsible for processing messages from a single invocation.
/// </summary>
internal sealed class OrderedInvocationMessageDispatcher : IInvocationMessageDispatcher, IDisposable
{
    private readonly ILogger _logger;
    private readonly string _invocationId;
    private readonly Action<InboundGrpcEvent> _processItemWithChannel;

    // Separated these out for easier testing of the flow.
    private readonly Action<InboundGrpcEvent> _processItemWithThreadPool;

    private Channel<InboundGrpcEvent> _channel;
    private bool _isChannelInitialized = false;
    private bool _invocationComplete = false;
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderedInvocationMessageDispatcher"/> class.
    /// </summary>
    /// <param name="invocationId">The function invocation id.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="processItem">A callback to be invoked when processing an item.</param>
    public OrderedInvocationMessageDispatcher(string invocationId, ILogger logger, Action<InboundGrpcEvent> processItem)
        : this(invocationId, logger, processItem, processItem)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderedInvocationMessageDispatcher"/> class. This constructor is only for testing purposes.
    /// It allows a different action to be called when using the fallback ThreadPool dispatch behavior. This allows the tests to validate that
    /// the fallback is correctly invoked.
    /// </summary>
    /// <param name="invocationId">The function invocation id.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="processItemWithChannel">A callback to be invoked when processing an item from the internal Channel.</param>
    /// <param name="processItemWithThreadPool">A callback to be invoked when processing an item on the ThreadPool. This is a fallback scenario.</param>
    internal OrderedInvocationMessageDispatcher(string invocationId, ILogger logger, Action<InboundGrpcEvent> processItemWithChannel,
        Action<InboundGrpcEvent> processItemWithThreadPool)
    {
        _logger = logger;
        _invocationId = invocationId;
        _processItemWithChannel = processItemWithChannel;
        _processItemWithThreadPool = processItemWithThreadPool;
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
        // This is not thread-safe, but it is always called in-order from the same thread, so no
        // need for locking.

        // The channel is only needed if we receive any RpcLog messages before we receive an
        // InvocationResponse message. If the only message we ever see is InvocationResponse, we know
        // the invocation is already complete and there's no need to use a channel for ordering, so skip
        // the initialization altogether. In DispatchToInvocationResponse, we'll fallback to use the ThreadPool
        // if the channel is not initialized.

        // We also do not want to initialize the channel if we're receiving an RpcLog after the invocation
        // has completed. In this case, the RpcLog will be dropped anyway, so there's no need to maintain
        // ordering with the channel.
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

            // Receiving an InvocationResponse signals that we're done with this invocation.
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
        ThreadPool.QueueUserWorkItem(state => _processItemWithThreadPool((InboundGrpcEvent)state), msg);

    private async Task ReadMessagesAsync()
    {
        try
        {
            await foreach (InboundGrpcEvent msg in _channel.Reader.ReadAllAsync())
            {
                // Assume the Action being called is already wrapped in a try/catch
                _processItemWithChannel(msg);
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