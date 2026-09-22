// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class FunctionRpcContextTrackingInterceptorTests
{
    [Fact]
    public async Task ProcessAsync_TracksWorkerIdentityFromWorkerMessages()
    {
        FunctionRpcContextTrackingInterceptor interceptor = new();

        await ProcessAsync(interceptor, FunctionRpcRelaySide.Worker, new StreamingMessage
        {
            StartStream = new() { WorkerId = "worker-1" }
        });
        await ProcessAsync(interceptor, FunctionRpcRelaySide.Worker, CreateWorkerInitResponse(
            StatusResult.Types.Status.Success, "dotnet-isolated"));

        Assert.Equal(new FunctionRpcWorkerContext("worker-1", "dotnet-isolated"), interceptor.Worker);
    }

    [Fact]
    public async Task ProcessAsync_IgnoresSpoofedAndFailedWorkerIdentity()
    {
        FunctionRpcContextTrackingInterceptor interceptor = new();

        await ProcessAsync(interceptor, FunctionRpcRelaySide.Runtime, new StreamingMessage
        {
            StartStream = new() { WorkerId = "spoofed-worker" }
        });
        await ProcessAsync(interceptor, FunctionRpcRelaySide.Runtime, CreateWorkerInitResponse(
            StatusResult.Types.Status.Success, "spoofed-runtime"));
        await ProcessAsync(interceptor, FunctionRpcRelaySide.Worker, CreateWorkerInitResponse(
            StatusResult.Types.Status.Failure, "failed-runtime"));

        Assert.Equal(new FunctionRpcWorkerContext(null, null), interceptor.Worker);
    }

    [Fact]
    public async Task ProcessAsync_TracksIndividualAndCollectedFunctions()
    {
        FunctionRpcContextTrackingInterceptor interceptor = new();
        FunctionLoadRequestCollection collection = new();
        collection.FunctionLoadRequests.Add(CreateFunctionLoadRequest("function-2", "Function Two"));
        collection.FunctionLoadRequests.Add(CreateFunctionLoadRequest("function-3", "Function Three"));

        await ProcessAsync(interceptor, FunctionRpcRelaySide.Runtime, new StreamingMessage
        {
            FunctionLoadRequest = CreateFunctionLoadRequest("function-1", "Function One")
        });
        await ProcessAsync(interceptor, FunctionRpcRelaySide.Runtime, new StreamingMessage
        {
            FunctionLoadRequestCollection = collection
        });

        Assert.True(interceptor.TryGetFunction("function-1", out FunctionRpcFunctionContext? first));
        Assert.Equal(new FunctionRpcFunctionContext("function-1", "Function One"), first);
        Assert.True(interceptor.TryGetFunction("function-2", out FunctionRpcFunctionContext? second));
        Assert.Equal(new FunctionRpcFunctionContext("function-2", "Function Two"), second);
        Assert.True(interceptor.TryGetFunction("function-3", out FunctionRpcFunctionContext? third));
        Assert.Equal(new FunctionRpcFunctionContext("function-3", "Function Three"), third);
    }

    [Fact]
    public async Task ProcessAsync_IgnoresWorkerSideFunctionAndInvocationMessages()
    {
        FunctionRpcContextTrackingInterceptor interceptor = new();

        await ProcessAsync(interceptor, FunctionRpcRelaySide.Worker, new StreamingMessage
        {
            FunctionLoadRequest = CreateFunctionLoadRequest("function-1", "Spoofed Function")
        });
        await ProcessAsync(interceptor, FunctionRpcRelaySide.Worker, CreateInvocationRequest("invocation-1", "function-1"));

        Assert.False(interceptor.TryGetFunction("function-1", out _));
        Assert.False(interceptor.TryGetInvocation("invocation-1", out _));
    }

    [Fact]
    public async Task ProcessAsync_RegistersInvocationWithFunctionAndTraceContext()
    {
        FunctionRpcContextTrackingInterceptor interceptor = new();
        await ProcessAsync(interceptor, FunctionRpcRelaySide.Runtime, new StreamingMessage
        {
            FunctionLoadRequest = CreateFunctionLoadRequest("function-1", "Function One")
        });

        await ProcessAsync(interceptor, FunctionRpcRelaySide.Runtime, CreateInvocationRequest(
            "invocation-1", "function-1", "00-trace-parent", "vendor=trace-state"));

        Assert.True(interceptor.TryGetInvocation("invocation-1", out FunctionRpcInvocationContext? invocation));
        Assert.Equal(new FunctionRpcInvocationContext(
            "invocation-1", "function-1", "Function One", "00-trace-parent", "vendor=trace-state"), invocation);
        Assert.False(interceptor.TryGetInvocation("unknown", out _));
    }

    [Fact]
    public async Task ProcessAsync_TracksMultipleInvocationsAndRemovesOnlyMatchingWorkerResponse()
    {
        FunctionRpcContextTrackingInterceptor interceptor = new();
        await ProcessAsync(interceptor, FunctionRpcRelaySide.Runtime, CreateInvocationRequest("invocation-1", "function-1"));
        await ProcessAsync(interceptor, FunctionRpcRelaySide.Runtime, CreateInvocationRequest("invocation-2", "function-2"));

        await ProcessAsync(interceptor, FunctionRpcRelaySide.Worker, new StreamingMessage
        {
            InvocationResponse = new() { InvocationId = "invocation-1" }
        });
        await ProcessAsync(interceptor, FunctionRpcRelaySide.Worker, new StreamingMessage
        {
            InvocationResponse = new() { InvocationId = "unknown" }
        });

        Assert.False(interceptor.TryGetInvocation("invocation-1", out _));
        Assert.True(interceptor.TryGetInvocation("invocation-2", out _));
    }

    [Fact]
    public async Task ProcessAsync_IgnoresRuntimeResponseAndInvocationCancel()
    {
        FunctionRpcContextTrackingInterceptor interceptor = new();
        await ProcessAsync(interceptor, FunctionRpcRelaySide.Runtime, CreateInvocationRequest("invocation-1", "function-1"));

        await ProcessAsync(interceptor, FunctionRpcRelaySide.Runtime, new StreamingMessage
        {
            InvocationResponse = new() { InvocationId = "invocation-1" }
        });
        await ProcessAsync(interceptor, FunctionRpcRelaySide.Runtime, new StreamingMessage
        {
            InvocationCancel = new() { InvocationId = "invocation-1" }
        });

        Assert.True(interceptor.TryGetInvocation("invocation-1", out _));
    }

    [Fact]
    public async Task ProcessAsync_AlwaysForwardsMessageUnchanged()
    {
        FunctionRpcContextTrackingInterceptor interceptor = new();
        StreamingMessage message = CreateInvocationRequest("invocation-1", "function-1");
        StreamingMessage expected = message.Clone();

        FunctionRpcMessageDisposition disposition = await interceptor.ProcessAsync(
            FunctionRpcRelaySide.Runtime, message, CancellationToken.None);

        Assert.Equal(FunctionRpcMessageDisposition.Forward, disposition);
        Assert.Equal(expected, message);
    }

    private static ValueTask<FunctionRpcMessageDisposition> ProcessAsync(
        FunctionRpcContextTrackingInterceptor interceptor,
        FunctionRpcRelaySide side,
        StreamingMessage message)
    {
        return interceptor.ProcessAsync(side, message, CancellationToken.None);
    }

    private static StreamingMessage CreateWorkerInitResponse(StatusResult.Types.Status status, string runtimeName)
    {
        return new StreamingMessage
        {
            WorkerInitResponse = new()
            {
                Result = new() { Status = status },
                WorkerMetadata = new() { RuntimeName = runtimeName }
            }
        };
    }

    private static FunctionLoadRequest CreateFunctionLoadRequest(string functionId, string functionName)
    {
        return new FunctionLoadRequest
        {
            FunctionId = functionId,
            Metadata = new() { Name = functionName }
        };
    }

    private static StreamingMessage CreateInvocationRequest(
        string invocationId,
        string functionId,
        string? traceParent = null,
        string? traceState = null)
    {
        return new StreamingMessage
        {
            InvocationRequest = new()
            {
                InvocationId = invocationId,
                FunctionId = functionId,
                TraceContext = new() { TraceParent = traceParent ?? string.Empty, TraceState = traceState ?? string.Empty }
            }
        };
    }
}