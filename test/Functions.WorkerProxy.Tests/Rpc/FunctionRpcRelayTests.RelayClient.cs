// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Xunit;
using GrpcRpcException = Grpc.Core.RpcException;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private sealed class RelayClient : IAsyncDisposable
    {
        private readonly GrpcChannel _channel;
        private readonly AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> _call;
        private int _disposed;

        public RelayClient(GrpcChannel channel, AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> call)
        {
            _channel = channel;
            _call = call;
        }

        public async Task WriteAsync(StreamingMessage message, CancellationToken cancellationToken)
        {
            await _call.RequestStream.WriteAsync(message, cancellationToken);
        }

        public async Task WriteAllAsync(IEnumerable<StreamingMessage> messages, CancellationToken cancellationToken)
        {
            foreach (StreamingMessage message in messages)
            {
                await WriteAsync(message, cancellationToken);
            }
        }

        public async Task<StreamingMessage> ReadAsync(CancellationToken cancellationToken)
        {
            bool hasMessage = await _call.ResponseStream.MoveNext(cancellationToken);
            Assert.True(hasMessage);

            return _call.ResponseStream.Current;
        }

        public async Task<IReadOnlyList<StreamingMessage>> ReadAsync(int count, CancellationToken cancellationToken)
        {
            List<StreamingMessage> messages = new(count);
            for (int i = 0; i < count; i++)
            {
                messages.Add(await ReadAsync(cancellationToken));
            }

            return messages;
        }

        public async Task CompleteRequestAsync(CancellationToken cancellationToken)
        {
            await _call.RequestStream.CompleteAsync().WaitAsync(cancellationToken);
        }

        public async Task<StatusCode> WaitForTerminationAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (await _call.ResponseStream.MoveNext(cancellationToken))
                {
                }

                return StatusCode.OK;
            }
            catch (GrpcRpcException exception)
            {
                return exception.StatusCode;
            }
        }

        public async Task<GrpcRpcException> WriteAndReadRejectionAsync(StreamingMessage message, CancellationToken cancellationToken)
        {
            try
            {
                await WriteAsync(message, cancellationToken);
                while (await _call.ResponseStream.MoveNext(cancellationToken))
                {
                }
            }
            catch (GrpcRpcException exception)
            {
                return exception;
            }

            throw new InvalidOperationException("The duplicate FunctionRpc stream was not rejected.");
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _call.Dispose();
                _channel.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}
