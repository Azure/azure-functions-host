// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

internal sealed partial class ExtensionRpcStreamDispatcher
{
    private sealed partial class DispatchCall
    {
        private readonly string _workerId;
        private readonly string _sessionId;
        private readonly string _shardId;
        private readonly string _callId;
        private readonly ExtensionRpcStart _start;
        private readonly IExtensionRpcEndpointRouter _endpointRouter;
        private readonly ChannelWriter<ExtensionRpcMessage> _outbound;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly CancellationToken _sessionCancellationToken;
        private readonly Channel<ExtensionRpcMessage> _inbound =
            Channel.CreateBounded<ExtensionRpcMessage>(
                new BoundedChannelOptions(32)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait,
                });

        private readonly CreditWindow _responseCredits;
        private readonly Lock _lifetimeLock = new();
        private readonly uint _maxDataChunkSize;
        private readonly ulong _maxMessageSize;
        private readonly Action _onComplete;
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly DateTimeOffset? _deadline;
        private int _completed;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="DispatchCall"/> class.
        /// </summary>
        /// <param name="workerId">The worker identifier associated with the stream.</param>
        /// <param name="sessionId">The extension RPC session identifier.</param>
        /// <param name="shardId">The physical stream identifier retained by the wire contract.</param>
        /// <param name="callId">The logical call identifier.</param>
        /// <param name="start">The call start message.</param>
        /// <param name="endpointRouter">The router for registered extension endpoints.</param>
        /// <param name="outbound">The writer for response lifecycle messages.</param>
        /// <param name="logger">The logger used for dispatch diagnostics.</param>
        /// <param name="sessionCancellationToken">A token that is cancelled when the stream session ends.</param>
        /// <param name="initialResponseWindow">The initial response byte credits granted by the proxy.</param>
        /// <param name="maxDataChunkSize">The negotiated maximum response chunk size.</param>
        /// <param name="maxMessageSize">The negotiated maximum response message size.</param>
        /// <param name="onComplete">The callback that removes the completed call from the dispatcher.</param>
        public DispatchCall(
            string workerId,
            string sessionId,
            string shardId,
            string callId,
            ExtensionRpcStart start,
            IExtensionRpcEndpointRouter endpointRouter,
            ChannelWriter<ExtensionRpcMessage> outbound,
            ILogger logger,
            CancellationToken sessionCancellationToken,
            ulong initialResponseWindow,
            uint maxDataChunkSize,
            ulong maxMessageSize,
            Action onComplete)
        {
            _workerId = workerId;
            _sessionId = sessionId;
            _shardId = shardId;
            _callId = callId;
            _start = start;
            _endpointRouter = endpointRouter;
            _outbound = outbound;
            _logger = logger;
            _sessionCancellationToken = sessionCancellationToken;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellationToken);
            if (start.Timeout is not null)
            {
                TimeSpan timeout = start.Timeout.ToTimeSpan();
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (timeout <= TimeSpan.Zero)
                {
                    _deadline = now;
                    _cancellationTokenSource.CancelAfter(TimeSpan.Zero);
                }
                else
                {
                    if (timeout <= DateTimeOffset.MaxValue - now)
                    {
                        _deadline = now.Add(timeout);
                    }

                    if (timeout <= MaxCancellationTimerDuration)
                    {
                        _cancellationTokenSource.CancelAfter(timeout);
                    }
                }
            }

            _responseCredits = new CreditWindow(initialResponseWindow);
            _maxDataChunkSize = maxDataChunkSize;
            _maxMessageSize = maxMessageSize;
            _onComplete = onComplete;
        }

        /// <summary>
        /// Gets the task that completes when endpoint dispatch and cleanup have finished.
        /// </summary>
        public Task Completion => _completion.Task;

        /// <summary>
        /// Starts endpoint dispatch for this logical call.
        /// </summary>
        public void Start()
        {
            _ = RunAsync();
        }

        /// <summary>
        /// Queues a request lifecycle message or applies a response flow-control update.
        /// </summary>
        /// <param name="message">The inbound message to process.</param>
        /// <param name="cancellationToken">A token that cancels queueing the message.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public ValueTask QueueInboundAsync(
            ExtensionRpcMessage message,
            CancellationToken cancellationToken)
        {
            if (message.ContentCase is ExtensionRpcMessage.ContentOneofCase.WindowUpdate)
            {
                _responseCredits.Add(message.WindowUpdate.ByteCount);

                return ValueTask.CompletedTask;
            }

            return _inbound.Writer.WriteAsync(message, cancellationToken);
        }

        /// <summary>
        /// Cancels endpoint dispatch and completes the inbound request queue.
        /// </summary>
        public void Cancel()
        {
            lock (_lifetimeLock)
            {
                if (!_disposed)
                {
                    _cancellationTokenSource.Cancel();
                }
            }

            _inbound.Writer.TryComplete();
        }

        private async Task RunAsync()
        {
            ExtensionRpcEndpoint? endpoint = null;
            try
            {
                endpoint = await _endpointRouter.RouteAsync(_workerId, _start.Method, _cancellationTokenSource.Token);
                if (endpoint is null)
                {
                    await TryCompleteAfterFailureAsync(
                        ExtensionRpcStatus.Unimplemented,
                        $"No extension gRPC endpoint is registered for '{_start.Method}'.",
                        []);

                    return;
                }

                using CancellationTokenRegistration registration = endpoint.CancellationToken.Register(Cancel);
                await InvokeEndpointAsync(endpoint, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
            {
                bool deadlineExceeded = _deadline is not null
                    && DateTimeOffset.UtcNow >= _deadline.Value;
                ExtensionRpcStatus status = deadlineExceeded
                    ? ExtensionRpcStatus.DeadlineExceeded
                    : ExtensionRpcStatus.Cancelled;
                string detail = deadlineExceeded
                    ? "The extension gRPC call deadline was exceeded."
                    : "The extension gRPC call was cancelled.";
                await TryCompleteAfterFailureAsync(status, detail, []);
            }
            catch (Exception exception)
            {
                Log.DispatchFailed(_logger, exception, _start.Method, _workerId);
                await TryCompleteAfterFailureAsync(
                    ExtensionRpcStatus.Internal,
                    "The extension gRPC endpoint failed.",
                    []);
            }
            finally
            {
                if (endpoint is not null)
                {
                    try
                    {
                        await endpoint.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        Log.EndpointLeaseReleaseFailed(_logger, exception, _callId);
                    }
                }

                _inbound.Writer.TryComplete();
                _onComplete();
                lock (_lifetimeLock)
                {
                    _disposed = true;
                    _cancellationTokenSource.Dispose();
                }

                _completion.TrySetResult();
            }
        }

        private async Task InvokeEndpointAsync(ExtensionRpcEndpoint endpoint, CancellationToken cancellationToken)
        {
            var requestPipe = new System.IO.Pipelines.Pipe();
            var responsePipe = new System.IO.Pipelines.Pipe();
            var trailersFeature = new ResponseTrailersFeature();
            var context = new DefaultHttpContext();
            await using AsyncServiceScope scope = endpoint.Services.CreateAsyncScope();
            context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseTrailersFeature>(trailersFeature);
            context.Request.Protocol = "HTTP/2";
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = _start.Method;
            context.Request.ContentType = GrpcContentType;
            context.Request.Body = requestPipe.Reader.AsStream();
            context.Response.Body = responsePipe.Writer.AsStream();
            context.RequestServices = scope.ServiceProvider;
            context.RequestAborted = cancellationToken;
            AddRequestMetadata(context.Request.Headers, _start.Metadata);

            Task requestTask = RelayRequestAsync(requestPipe.Writer, cancellationToken);
            Task endpointTask = InvokeEndpointCoreAsync(endpoint.RequestDelegate, context, responsePipe.Writer);
            Task responseTask = RelayResponseAsync(
                context.Response,
                trailersFeature,
                responsePipe.Reader,
                endpointTask,
                cancellationToken);

            try
            {
                Task completedTask = await Task.WhenAny(
                    requestTask,
                    endpointTask,
                    responseTask);
                if (completedTask.IsFaulted)
                {
                    await completedTask;
                }

                await endpointTask;
                await responseTask;
                _cancellationTokenSource.Cancel();

                try
                {
                    await requestTask;
                }
                catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
                {
                }
            }
            finally
            {
                Cancel();
                await Task.WhenAll(
                    ObserveCallTaskAsync(requestTask),
                    ObserveCallTaskAsync(endpointTask),
                    ObserveCallTaskAsync(responseTask));
            }
        }

        private static async Task InvokeEndpointCoreAsync(
            RequestDelegate requestDelegate,
            HttpContext context,
            System.IO.Pipelines.PipeWriter responseWriter)
        {
            Exception? error = null;
            try
            {
                await requestDelegate(context);
                await context.Response.CompleteAsync();
            }
            catch (Exception exception)
            {
                error = exception;
                throw;
            }
            finally
            {
                await responseWriter.CompleteAsync(error);
            }
        }

        private async Task ObserveCallTaskAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                Log.CallTaskStopped(_logger, exception, _callId);
            }
        }
    }
}
