// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Identity;

// Waiters can precede both RPC peers. Sent requests belong only to their actual runtime transport.
internal sealed class IdentityProxyService(IOptions<IdentityProxyOptions> options, TimeProvider timeProvider,
    ILogger<IdentityProxyService> logger)
{
    private readonly Lock _lock = new();
    private readonly IdentityProxyOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger _logger = logger;
    private TaskCompletionSource<Connection> _available = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Connection? _connection;
    private bool _disconnected;

    internal HttpProxyAdmission Admission { get; } = new(options.Value.MaxPendingRequests, options.Value.MaxBufferedBytes);

    internal bool Enabled => _options.Enabled;

    internal Connection Attach(Func<StreamingMessage, CancellationToken, Func<bool>?, Task<bool>> send, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_connection is not null)
            {
                throw new InvalidOperationException("An identity proxy transport is already attached.");
            }

            Connection connection = new(this, send, cancellationToken);
            _connection = connection;
            _disconnected = false;
            _available.TrySetResult(connection);
            return connection;
        }
    }

    internal async Task<HttpProxyMessage> SendAsync(HttpProxyRequest request, HttpProxyAdmission.Lease ownership,
        long started, CancellationToken cancellationToken)
    {
        Task<Connection> available;
        lock (_lock)
        {
            if (_disconnected)
            {
                throw new HttpProxyException(HttpProxyFailure.Types.Code.Unavailable);
            }

            available = _available.Task;
        }

        Connection connection = await available.WaitAsync(cancellationToken);
        HttpProxyLimits limits = await connection.Ready.WaitAsync(cancellationToken);
        if (request.Body.Length > limits.MaxRequestBodyBytes)
        {
            throw new HttpProxyException(HttpProxyFailure.Types.Code.RequestTooLarge);
        }

        HttpProxyProtocol.ValidateTarget(request.Target, _options.TokenPath, limits);
        List<HttpProxyHeader> headers = HttpProxyProtocol.FilterHeaders(request.Headers, limits);
        request.Headers.Clear();
        request.Headers.Add(headers);

        Task<HttpProxyMessage> forwarded = connection.SendAsync(request, ownership.Retain(), started, cancellationToken);
        return await forwarded.WaitAsync(cancellationToken);
    }

    internal sealed class Connection : IDisposable
    {
        private readonly IdentityProxyService _owner;
        private readonly Func<StreamingMessage, CancellationToken, Func<bool>?, Task<bool>> _send;
        private readonly CancellationToken _transport;
        private readonly Lock _lock = new();
        private readonly TaskCompletionSource<HttpProxyLimits> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, TaskCompletionSource<HttpProxyMessage>> _pending = new(StringComparer.Ordinal);
        private string? _sessionId;
        private HttpProxyLimits? _limits;
        private bool _closed;

        internal Connection(IdentityProxyService owner,
            Func<StreamingMessage, CancellationToken, Func<bool>?, Task<bool>> send, CancellationToken transport)
        {
            _owner = owner;
            _send = send;
            _transport = transport;
        }

        internal Task<HttpProxyLimits> Ready => _ready.Task;

        internal async Task ReceiveAsync(HttpProxyMessage message)
        {
            lock (_lock)
            {
                if (_closed)
                {
                    return;
                }
            }

            if (message.Hello is { } hello && _sessionId is null)
            {
                if (hello.Version != HttpProxyProtocol.Version || !Guid.TryParseExact(message.SessionId, "N", out _)
                    || !string.IsNullOrEmpty(message.RequestId))
                {
                    throw new InvalidDataException("Unsupported HTTP proxy bootstrap.");
                }

                HttpProxyLimits limits = _owner._options.Negotiate(hello.Limits);
                _sessionId = message.SessionId;
                _limits = limits;
                await _send(new StreamingMessage
                {
                    HttpProxy = new()
                    {
                        SessionId = _sessionId,
                        Ready = new() { Version = HttpProxyProtocol.Version, Limits = limits }
                    }
                }, _transport, null);
                _ready.TrySetResult(limits);
                return;
            }

            if (_limits is null || !string.Equals(_sessionId, message.SessionId, StringComparison.Ordinal)
                || message.ContentCase is not (HttpProxyMessage.ContentOneofCase.Response or HttpProxyMessage.ContentOneofCase.Failure))
            {
                throw new InvalidDataException("Unexpected HTTP proxy response.");
            }

            TaskCompletionSource<HttpProxyMessage>? completion;
            lock (_lock)
            {
                _pending.TryGetValue(message.RequestId, out completion);
            }

            if (completion is null)
            {
                return;
            }

            if (message.Response is { } response)
            {
                if (response.StatusCode is < 200 or > 599 || response.Body.Length > _limits.MaxResponseBodyBytes
                    || (response.HasContentLength && response.ContentLength < 0))
                {
                    throw new InvalidDataException("Invalid HTTP proxy response bounds.");
                }

                List<HttpProxyHeader> headers = HttpProxyProtocol.FilterHeaders(response.Headers, _limits);
                response.Headers.Clear();
                response.Headers.Add(headers);
            }

            completion.TrySetResult(message);
        }

        internal async Task<HttpProxyMessage> SendAsync(HttpProxyRequest request, IDisposable ownership, long started,
            CancellationToken cancellationToken)
        {
            using (ownership)
            {
                string requestId = Guid.NewGuid().ToString("N");
                string sessionId = _sessionId ?? throw new InvalidOperationException("The HTTP proxy has not initialized.");
                HttpProxyLimits limits = _limits ?? throw new InvalidOperationException("The HTTP proxy has not initialized.");
                TaskCompletionSource<HttpProxyMessage> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_lock)
                {
                    if (_closed)
                    {
                        return Failure(HttpProxyFailure.Types.Code.Unavailable);
                    }

                    if (_pending.Count >= limits.MaxPendingRequests)
                    {
                        return Failure(HttpProxyFailure.Types.Code.TooManyRequests);
                    }

                    _pending.Add(requestId, completion);
                }

                try
                {
                    bool sent = await _send(new StreamingMessage
                    {
                        HttpProxy = new() { SessionId = sessionId, RequestId = requestId, Request = request }
                    }, cancellationToken, () =>
                    {
                        TimeSpan remaining = _owner._options.RequestTimeout - _owner._timeProvider.GetElapsedTime(started);
                        if (remaining <= TimeSpan.Zero)
                        {
                            return false;
                        }

                        request.TimeoutMs = (uint)Math.Max(1, Math.Ceiling(remaining.TotalMilliseconds));
                        return true;
                    });
                    if (!sent)
                    {
                        return Failure(HttpProxyFailure.Types.Code.Cancelled);
                    }

                    try
                    {
                        return await completion.Task.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        await _send(new StreamingMessage
                        {
                            HttpProxy = new() { SessionId = sessionId, RequestId = requestId, Cancel = new() }
                        }, _transport, null);
                        // The HTTP caller has left; retain admission until the peer's terminal acknowledgment.
                        return await completion.Task;
                    }
                }
                catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or IOException
                    or System.Threading.Channels.ChannelClosedException or Grpc.Core.RpcException)
                {
                    _owner._logger.LogDebug("Identity proxy request ended with its transport ({ErrorType}).", exception.GetType().Name);
                    return Failure(HttpProxyFailure.Types.Code.Unavailable);
                }
                finally
                {
                    lock (_lock)
                    {
                        _pending.Remove(requestId);
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _closed = true;
                _ready.TrySetCanceled(new CancellationToken(canceled: true));
                foreach (TaskCompletionSource<HttpProxyMessage> pending in _pending.Values)
                {
                    pending.TrySetResult(Failure(HttpProxyFailure.Types.Code.Unavailable));
                }
            }

            lock (_owner._lock)
            {
                if (ReferenceEquals(_owner._connection, this))
                {
                    _owner._connection = null;
                    _owner._disconnected = true;
                    _owner._available = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        private static HttpProxyMessage Failure(HttpProxyFailure.Types.Code code) => new() { Failure = new() { Code = code } };
    }
}
