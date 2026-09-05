// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Host.Tests;

internal sealed class WorkerLinkTestRpcServer : IAsyncDisposable
{
    private readonly WebApplication _application;
    private readonly TestFunctionRpcService _service;

    private WorkerLinkTestRpcServer(WebApplication application, Uri endpoint, TestFunctionRpcService service)
    {
        _application = application;
        _service = service;
        Endpoint = endpoint;
    }

    internal Uri Endpoint { get; }

    internal int StreamCount => _service.StreamCount;

    internal static async Task<WorkerLinkTestRpcServer> StartAsync(CancellationToken cancellationToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        TestFunctionRpcService service = new();
        builder.Services.AddSingleton(service);
        builder.Services.AddGrpc();

        WebApplication application = builder.Build();
        application.MapGrpcService<TestFunctionRpcService>();
        try
        {
            await application.StartAsync(cancellationToken);
            IServer server = application.Services.GetRequiredService<IServer>();
            IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("Kestrel did not publish a loopback endpoint.");

            return new WorkerLinkTestRpcServer(application, new Uri(addresses.Addresses.Single()), service);
        }
        catch
        {
            await application.DisposeAsync();
            throw;
        }
    }

    internal Handshake Enqueue(string workerId)
    {
        Handshake handshake = new(workerId);
        _service.Handshakes.Enqueue(handshake);

        return handshake;
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await _application.StopAsync(timeout.Token);
        }
        finally
        {
            await _application.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    internal sealed class Handshake(string workerId)
    {
        internal string WorkerId { get; } = workerId;

        internal TaskCompletionSource InitRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource StreamClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<WorkerInitResponse> InitResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task InitReceived => InitRequest.Task;

        internal Task Disconnected => StreamClosed.Task;

        internal void Succeed()
            => InitResponse.SetResult(new WorkerInitResponse { Result = new StatusResult { Status = StatusResult.Types.Status.Success } });

        internal void Fail(string detail)
            => InitResponse.SetResult(new WorkerInitResponse
            {
                Result = new StatusResult
                {
                    Status = StatusResult.Types.Status.Failure,
                    Exception = new() { Message = detail, StackTrace = detail },
                },
            });
    }

    private sealed class TestFunctionRpcService : FunctionRpc.FunctionRpcBase
    {
        private int _streamCount;

        internal ConcurrentQueue<Handshake> Handshakes { get; } = new();

        internal int StreamCount => Volatile.Read(ref _streamCount);

        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream,
            IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            Interlocked.Increment(ref _streamCount);
            if (!Handshakes.TryDequeue(out Handshake? handshake))
            {
                throw new Grpc.Core.RpcException(new Status(StatusCode.FailedPrecondition, "Unexpected test worker connection."));
            }

            try
            {
                await responseStream.WriteAsync(new StreamingMessage
                {
                    StartStream = new StartStream { WorkerId = handshake.WorkerId },
                });

                while (await requestStream.MoveNext(context.CancellationToken))
                {
                    StreamingMessage request = requestStream.Current;
                    if (request.ContentCase is StreamingMessage.ContentOneofCase.WorkerInitRequest)
                    {
                        handshake.InitRequest.TrySetResult();
                        WorkerInitResponse response = await handshake.InitResponse.Task.WaitAsync(context.CancellationToken);
                        await responseStream.WriteAsync(new StreamingMessage
                        {
                            RequestId = request.RequestId,
                            WorkerInitResponse = response,
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                handshake.StreamClosed.TrySetResult();
            }
        }
    }
}
