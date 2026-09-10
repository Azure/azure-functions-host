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
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Host.Tests;

/// <summary>
/// Simulates WorkerProxy's runtime-facing initialization protocol over a real gRPC listener.
/// </summary>
internal sealed class FakeWorkerProxyGrpcServer : IAsyncDisposable
{
    private readonly WebApplication _application;
    private readonly TestFunctionRpcService _service;

    private FakeWorkerProxyGrpcServer(WebApplication application, Uri endpoint, TestFunctionRpcService service)
    {
        _application = application;
        _service = service;
        Endpoint = endpoint;
    }

    internal Uri Endpoint { get; }

    internal int StreamCount => _service.StreamCount;

    internal static async Task<FakeWorkerProxyGrpcServer> StartAsync(CancellationToken cancellationToken)
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

            return new FakeWorkerProxyGrpcServer(application, new Uri(addresses.Addresses.Single()), service);
        }
        catch
        {
            await application.DisposeAsync();
            throw;
        }
    }

    // Replies successfully to initialization without manual test coordination.
    internal TestWorker AddWorker(string workerId, string? httpUri = null)
    {
        TestWorker worker = AddPendingWorker(workerId);
        worker.CompleteInitialization(httpUri);

        return worker;
    }

    // Leaves initialization pending until the test explicitly completes or fails it.
    internal TestWorker AddPendingWorker(string workerId)
    {
        TestWorker worker = new(workerId);
        _service.Workers.Enqueue(worker);

        return worker;
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

    internal sealed class TestWorker(string workerId)
    {
        private readonly TaskCompletionSource _initializationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<WorkerInitResponse> _initializationResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal string WorkerId { get; } = workerId;

        internal Task InitializationStarted => _initializationStarted.Task;

        internal Task Disconnected => _disconnected.Task;

        internal void CompleteInitialization(string? httpUri = null)
        {
            WorkerInitResponse response = new() { Result = new StatusResult { Status = StatusResult.Types.Status.Success } };
            if (httpUri is not null)
            {
                response.Capabilities.Add(RpcWorkerConstants.HttpUri, httpUri);
            }

            _initializationResponse.SetResult(response);
        }

        internal void FailInitialization(string detail)
            => _initializationResponse.SetResult(new WorkerInitResponse
            {
                Result = new StatusResult
                {
                    Status = StatusResult.Types.Status.Failure,
                    Exception = new() { Message = detail, StackTrace = detail },
                },
            });

        internal Task<WorkerInitResponse> GetInitializationResponseAsync(CancellationToken cancellationToken)
        {
            _initializationStarted.TrySetResult();
            return _initializationResponse.Task.WaitAsync(cancellationToken);
        }

        internal void MarkDisconnected() => _disconnected.TrySetResult();
    }

    private sealed class TestFunctionRpcService : FunctionRpc.FunctionRpcBase
    {
        private int _streamCount;

        internal ConcurrentQueue<TestWorker> Workers { get; } = new();

        internal int StreamCount => Volatile.Read(ref _streamCount);

        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream,
            IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            Interlocked.Increment(ref _streamCount);
            if (!Workers.TryDequeue(out TestWorker? worker))
            {
                throw new Grpc.Core.RpcException(new Status(StatusCode.FailedPrecondition, "Unexpected test worker connection."));
            }

            try
            {
                await responseStream.WriteAsync(new StreamingMessage
                {
                    StartStream = new StartStream { WorkerId = worker.WorkerId },
                });

                while (await requestStream.MoveNext(context.CancellationToken))
                {
                    StreamingMessage request = requestStream.Current;
                    if (request.ContentCase is StreamingMessage.ContentOneofCase.WorkerInitRequest)
                    {
                        WorkerInitResponse response = await worker.GetInitializationResponseAsync(context.CancellationToken);
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
                worker.MarkDisconnected();
            }
        }
    }
}
