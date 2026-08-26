// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Adapts an SDK <see cref="AsyncDuplexStreamingCall{TRequest, TResponse}"/> to <see cref="IDuplexCall{TRequest, TResponse}"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal sealed class GrpcDuplexCall<TRequest, TResponse> : IDuplexCall<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    private readonly AsyncDuplexStreamingCall<TRequest, TResponse> _call;
    private readonly CancellationTokenSource _callLifetimeSource;
    private readonly ILogger _logger;
    private readonly IDisposable _ownedResource;
    private readonly object _syncLock = new();
    private Task _disposeTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDuplexCall{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="call">The SDK duplex call.</param>
    /// <param name="callLifetimeSource">The cancellation source used to create <paramref name="call"/>.</param>
    /// <param name="ownedResource">The underlying connection resource.</param>
    /// <param name="logger">The logger used for secondary cleanup failures.</param>
    internal GrpcDuplexCall(AsyncDuplexStreamingCall<TRequest, TResponse> call, CancellationTokenSource callLifetimeSource,
        IDisposable ownedResource, ILogger logger)
    {
        _call = call ?? throw new ArgumentNullException(nameof(call));
        _callLifetimeSource = callLifetimeSource ?? throw new ArgumentNullException(nameof(callLifetimeSource));
        _ownedResource = ownedResource ?? throw new ArgumentNullException(nameof(ownedResource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task WriteAsync(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _call.RequestStream.WriteAsync(request);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TResponse> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await _call.ResponseStream.MoveNext(cancellationToken))
        {
            yield return _call.ResponseStream.Current;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_syncLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Exception cleanupException = null;

        try
        {
            await _callLifetimeSource.CancelAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "gRPC duplex call cleanup failed while cancelling the call lifetime.");
            cleanupException = exception;
        }

        cleanupException = CaptureCleanupFailure(cleanupException, _call.Dispose, "dispose the SDK duplex call");
        cleanupException = CaptureCleanupFailure(cleanupException, _ownedResource.Dispose, "dispose the connection resource");
        cleanupException = CaptureCleanupFailure(cleanupException, _callLifetimeSource.Dispose, "dispose the call lifetime");

        if (cleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }
    }

    private Exception CaptureCleanupFailure(Exception currentException, Action cleanup, string operation)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "gRPC duplex call cleanup failed while attempting to {CleanupOperation}.", operation);
            return currentException is null ? exception : new AggregateException(currentException, exception);
        }

        return currentException;
    }
}
