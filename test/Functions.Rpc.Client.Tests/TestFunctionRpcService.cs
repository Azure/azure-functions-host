// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class TestFunctionRpcService : FunctionRpc.FunctionRpcBase
{
    private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Channel<StreamingMessage> _requests = Channel.CreateUnbounded<StreamingMessage>();
    private readonly Channel<StreamingMessage> _responses = Channel.CreateUnbounded<StreamingMessage>();

    internal Task Connected => _connected.Task;

    internal Task Disconnected => _disconnected.Task;

    internal ChannelReader<StreamingMessage> Requests => _requests.Reader;

    public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream,
        IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
    {
        using CancellationTokenSource streamSource = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        _connected.TrySetResult();

        Task readTask = ReadRequestsAsync(requestStream, streamSource.Token);
        Task writeTask = WriteResponsesAsync(responseStream, streamSource.Token);

        try
        {
            Task firstCompletedTask = await Task.WhenAny(readTask, writeTask);
            await firstCompletedTask;
        }
        finally
        {
            streamSource.Cancel();
            await ObserveCancellationAsync(readTask);
            await ObserveCancellationAsync(writeTask);
            _requests.Writer.TryComplete();
            _disconnected.TrySetResult();
        }
    }

    internal ValueTask SendResponseAsync(StreamingMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _responses.Writer.WriteAsync(message, cancellationToken);
    }

    internal void CompleteResponses(Exception exception = null) => _responses.Writer.TryComplete(exception);

    private static async Task ObserveCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReadRequestsAsync(IAsyncStreamReader<StreamingMessage> requestStream, CancellationToken cancellationToken)
    {
        while (await requestStream.MoveNext(cancellationToken))
        {
            await _requests.Writer.WriteAsync(requestStream.Current, cancellationToken);
        }
    }

    private async Task WriteResponsesAsync(IServerStreamWriter<StreamingMessage> responseStream, CancellationToken cancellationToken)
    {
        await foreach (StreamingMessage message in _responses.Reader.ReadAllAsync(cancellationToken))
        {
            await responseStream.WriteAsync(message);
        }
    }
}
