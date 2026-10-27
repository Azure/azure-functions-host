// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Services reverse HTTP controls on the root-owned transport before the customer worker or ScriptHost initializes.
/// </summary>
/// <remarks>Ordinary worker messages retain their existing channel semantics and share the same sole gRPC writer.</remarks>
internal sealed class HttpProxyDuplexChannel : DuplexChannel<StreamingMessage>
{
    private readonly Lock _requestStateLock = new();
    private readonly GrpcDuplexChannel<StreamingMessage> _inner;
    private readonly IHttpProxyRequestHandler _handler;
    private readonly HttpProxyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly HttpProxyAdmission _hostAdmission;
    private readonly HttpProxyAdmission _transportAdmission;
    private readonly ILogger _logger;
    private readonly Channel<StreamingMessage> _inbound = Channel.CreateUnbounded<StreamingMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = false });

    private readonly Dictionary<string, PendingRequest> _requests = new(StringComparer.Ordinal);
    // Request IDs retire before terminal delivery; operations retain both budgets until the physical write ends.
    private readonly HashSet<PendingRequest> _operations = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly Task _readTask;
    private readonly Task _helloTask;
    private HttpProxyLimits _limits;

    internal HttpProxyDuplexChannel(GrpcDuplexChannel<StreamingMessage> inner, IHttpProxyRequestHandler handler,
        HttpProxyOptions options, TimeProvider timeProvider, HttpProxyAdmission hostAdmission, ILogger logger)
    {
        _inner = inner;
        _handler = handler;
        _options = options;
        _timeProvider = timeProvider;
        _hostAdmission = hostAdmission;
        _transportAdmission = new(options.MaxPendingRequests, options.MaxBufferedBytes);
        _logger = logger;
        Reader = _inbound.Reader;
        Writer = inner.Writer;
        _readTask = ReadAsync();
        _helloTask = SendHelloAsync();
    }

    /// <summary>
    /// Negotiates proxy protocol support and limits independently of the customer's StartStream/WorkerInit handshake.
    /// </summary>
    private async Task SendHelloAsync()
    {
        try
        {
            await _inner.WriteTrackedAsync(new StreamingMessage
            {
                HttpProxy = new()
                {
                    SessionId = _sessionId,
                    Hello = new() { Version = HttpProxyProtocol.Version, Limits = _options.ToLimits() }
                }
            }, _shutdown.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
            or ChannelClosedException or Grpc.Core.RpcException or IOException)
        {
            _logger.LogDebug("HTTP proxy bootstrap ended because its transport closed.");
        }
    }

    /// <summary>
    /// Consumes proxy controls without blocking the stream reader on upstream HTTP work.
    /// </summary>
    private async Task ReadAsync()
    {
        Exception error = null;
        try
        {
            await foreach (StreamingMessage message in _inner.Reader.ReadAllAsync(_shutdown.Token))
            {
                if (message.HttpProxy is { } proxy)
                {
                    Process(proxy);
                }
                else
                {
                    await _inbound.Writer.WriteAsync(message, _shutdown.Token);
                }
            }
        }
        catch (Exception) when (_shutdown.IsCancellationRequested)
        {
            _logger.LogDebug("HTTP proxy receive loop stopped with its transport.");
        }
        catch (Exception exception)
        {
            error = exception;
            _logger.LogWarning("HTTP proxy transport failed ({ErrorType}).", exception.GetType().Name);
        }
        finally
        {
            _inbound.Writer.TryComplete(error);
            await _shutdown.CancelAsync();
        }
    }

    /// <summary>
    /// Binds controls to this transport session and dispatches request or cancellation work.
    /// </summary>
    private void Process(HttpProxyMessage message)
    {
        if (!string.Equals(message.SessionId, _sessionId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The HTTP proxy session does not match this transport.");
        }

        if (message.Ready is { } ready && _limits is null && string.IsNullOrEmpty(message.RequestId))
        {
            if (ready.Version != HttpProxyProtocol.Version)
            {
                throw new InvalidDataException("Unsupported HTTP proxy protocol version.");
            }

            _limits = _options.Negotiate(ready.Limits);
            return;
        }

        if (_limits is null || !Guid.TryParseExact(message.RequestId, "N", out _))
        {
            throw new InvalidDataException("Unexpected HTTP proxy control message.");
        }

        if (message.Cancel is not null)
        {
            PendingRequest canceled;
            lock (_requestStateLock)
            {
                _requests.TryGetValue(message.RequestId, out canceled);
            }

            canceled?.Cancel();
            return;
        }

        if (message.Request is not { } request)
        {
            throw new InvalidDataException("The peer sent a control message in the wrong direction.");
        }

        PendingRequest pending = new();
        lock (_requestStateLock)
        {
            if (_requests.Count >= _limits.MaxPendingRequests || !_requests.TryAdd(message.RequestId, pending))
            {
                throw new InvalidDataException("The peer exceeded its HTTP proxy admission limit or reused a request ID.");
            }

            _operations.Add(pending);
        }

        _ = ExecuteAsync(message.RequestId, request, pending);
    }

    /// <summary>
    /// Owns an admitted operation through its single terminal response, including the response's physical write/discard.
    /// </summary>
    private async Task ExecuteAsync(string requestId, HttpProxyRequest request, PendingRequest pending)
    {
        HttpProxyMessage terminal = new() { SessionId = _sessionId, RequestId = requestId };
        try
        {
            TimeSpan timeout = TimeSpan.FromMilliseconds(Math.Min(request.TimeoutMs, _options.RequestTimeout.TotalMilliseconds));
            using CancellationTokenSource deadline = new(timeout, _timeProvider);
            using CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(
                deadline.Token, pending.Token, _shutdown.Token);
            using IDisposable transportAdmission = _transportAdmission.TryAcquire(_options.ReservationBytes);
            using IDisposable hostAdmission = transportAdmission is not null
                ? _hostAdmission.TryAcquire(_options.ReservationBytes)
                : null;
            try
            {
                if (transportAdmission is null || hostAdmission is null)
                {
                    throw new HttpProxyException(HttpProxyFailure.Types.Code.TooManyRequests);
                }

                if (request.TimeoutMs == 0 || request.Body.Length > _limits.MaxRequestBodyBytes
                    || request.Target.Length > _limits.MaxTargetBytes || !HttpProxyProtocol.IsToken(request.Method))
                {
                    throw new HttpProxyException(HttpProxyFailure.Types.Code.InvalidRequest);
                }

                terminal.Response = await _handler.SendAsync(request, _limits, operation.Token);
                if (terminal.Response.StatusCode is < 200 or > 599 || terminal.Response.Body.Length > _limits.MaxResponseBodyBytes
                    || (terminal.Response.HasContentLength && terminal.Response.ContentLength < 0))
                {
                    throw new HttpProxyException(HttpProxyFailure.Types.Code.BadGateway);
                }

                List<HttpProxyHeader> headers = HttpProxyProtocol.FilterHeaders(terminal.Response.Headers, _limits);
                terminal.Response.Headers.Clear();
                terminal.Response.Headers.Add(headers);
            }
            catch (HttpProxyException exception)
            {
                terminal.Failure = new() { Code = exception.Code };
            }
            catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
            {
                terminal.Failure = new()
                {
                    Code = pending.Token.IsCancellationRequested
                        ? HttpProxyFailure.Types.Code.Cancelled : HttpProxyFailure.Types.Code.DeadlineExceeded
                };
            }
            catch (Exception exception) when (!_shutdown.IsCancellationRequested)
            {
                _logger.LogError("HTTP proxy handler failed ({ErrorType}).", exception.GetType().Name);
                terminal.Failure = new() { Code = HttpProxyFailure.Types.Code.BadGateway };
            }

            if (!_shutdown.IsCancellationRequested)
            {
                await _inner.WriteTrackedAsync(new() { HttpProxy = terminal }, _shutdown.Token, () =>
                {
                    // A peer can receive the terminal frame and send its next request before WriteAsync returns.
                    // Retire the request ID before delivery, but keep both admission leases until the write finishes.
                    lock (_requestStateLock)
                    {
                        if (_requests.TryGetValue(requestId, out PendingRequest current) && ReferenceEquals(current, pending))
                        {
                            _requests.Remove(requestId);
                        }
                    }

                    return true;
                });
            }
        }
        catch (Exception exception) when (_shutdown.IsCancellationRequested)
        {
            _logger.LogDebug("HTTP proxy request cleanup ended with its transport ({ErrorType}).", exception.GetType().Name);
        }
        catch (Exception exception)
        {
            _logger.LogError("HTTP proxy terminal response could not be delivered ({ErrorType}).", exception.GetType().Name);
            _inbound.Writer.TryComplete(exception);
            await _shutdown.CancelAsync();
        }
        finally
        {
            lock (_requestStateLock)
            {
                if (_requests.TryGetValue(requestId, out PendingRequest current) && ReferenceEquals(current, pending))
                {
                    _requests.Remove(requestId);
                }

                _operations.Remove(pending);
            }

            pending.Dispose();
            pending.Completed.TrySetResult();
        }
    }

    /// <summary>
    /// Waits for the currently owned operations, including any terminal messages still queued for delivery.
    /// </summary>
    internal Task WaitForPendingOperationsAsync()
    {
        lock (_requestStateLock)
        {
            return Task.WhenAll(_operations.Select(static request => request.Completed.Task));
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        await _shutdown.CancelAsync();
        await _inner.DisposeAsync();
        await Task.WhenAll(_readTask, _helloTask);
        await WaitForPendingOperationsAsync();
        _shutdown.Dispose();
    }

    private sealed class PendingRequest : IDisposable
    {
        private readonly Lock _lock = new();
        private readonly CancellationTokenSource _cancel = new();
        private bool _disposed;

        internal CancellationToken Token => _cancel.Token;

        internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Cancel()
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _cancel.Cancel();
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                _cancel.Dispose();
            }
        }
    }
}
