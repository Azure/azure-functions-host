// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Fact]
    public async Task Relay_LogsLinkAndStartupBoundariesWithoutInvocationTelemetry()
    {
        const string sentinel = "customer-payload-secret";
        const string invocationId = "invocation-id";
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(generator);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await WaitForLogAsync(() => generator.ProxyRecords.Any(record =>
            string.Equals(record.EventName, "PeerAttached", StringComparison.Ordinal)), timeout.Token);
        CapturedProxyLog attached = Assert.Single(generator.ProxyRecords, record =>
            string.Equals(record.EventName, "PeerAttached", StringComparison.Ordinal));
        Assert.Null(attached.Context!.WorkerId);
        StreamingMessage startStream = CreateStartStream();
        startStream.StartStream.WorkerId = "telemetry-worker";
        await worker.WriteAsync(startStream, timeout.Token);
        await WaitForLogAsync(() => generator.ProxyRecords.Any(record =>
            string.Equals(record.EventName, "StartStreamValidated", StringComparison.Ordinal)), timeout.Token);
        CapturedProxyLog preLink = Assert.Single(generator.ProxyRecords, record =>
            string.Equals(record.EventName, "StartStreamValidated", StringComparison.Ordinal));
        Assert.Equal("telemetry-worker", preLink.Context!.WorkerId);
        Assert.Equal(attached.Context.ProxySessionId, preLink.Context.ProxySessionId);

        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        StreamingMessage forwardedStart = await runtime.ReadAsync(timeout.Token);
        Assert.Equal("telemetry-worker", forwardedStart.StartStream.WorkerId);
        StreamingMessage init = new() { WorkerInitRequest = new() };
        await runtime.WriteAsync(init, timeout.Token);
        Assert.Equal(init, await worker.ReadAsync(timeout.Token));
        StreamingMessage initialized = CreateInitResponse("init", httpUri: null);
        await worker.WriteAsync(initialized, timeout.Token);
        await runtime.ReadAsync(timeout.Token);

        StreamingMessage loadRequest = new() { FunctionLoadRequest = new() { FunctionId = sentinel } };
        StreamingMessage loadResponse = new()
        {
            FunctionLoadResponse = new()
            {
                FunctionId = sentinel,
                Result = new()
                {
                    Status = StatusResult.Types.Status.Failure,
                    Exception = new() { Message = sentinel, StackTrace = sentinel }
                }
            }
        };
        await runtime.WriteAsync(loadRequest, timeout.Token);
        Assert.Equal(loadRequest, await worker.ReadAsync(timeout.Token));
        await worker.WriteAsync(loadResponse, timeout.Token);
        Assert.Equal(loadResponse, await runtime.ReadAsync(timeout.Token));
        await WaitForLogAsync(() => generator.ProxyRecords.Count(record =>
            string.Equals(record.EventName, "ProtocolMessageObserved", StringComparison.Ordinal)) == 8, timeout.Token);

        foreach (string operation in new[] { "Initialization", "FunctionLoad" })
        {
            CapturedProxyLog[] records = generator.ProxyRecords.Where(record =>
                string.Equals(record.Context?.Operation, operation, StringComparison.Ordinal)).ToArray();
            Assert.Equal(new[] { "RequestForwarded", "RequestReceived", "ResponseForwarded", "ResponseReceived" },
                records.Select(record => record.Context!.Stage).Order(StringComparer.Ordinal));
            Assert.All(records, record =>
            {
                Assert.Equal("telemetry-worker", record.Context!.WorkerId);
                Assert.Equal(preLink.Context.ProxySessionId, record.Context.ProxySessionId);
            });
            Assert.All(records.Where(record => record.Context!.Stage!.StartsWith("Response", StringComparison.Ordinal)),
                record => Assert.Equal(string.Equals(operation, "Initialization", StringComparison.Ordinal) ? "Success" : "Failure",
                    record.Context!.Attributes!.RpcStatus));
        }

        StreamingMessage request = new()
        {
            InvocationRequest = new()
            {
                InvocationId = invocationId,
                FunctionId = sentinel,
                TriggerMetadata = { ["secret"] = new TypedData { String = sentinel } }
            }
        };
        StreamingMessage response = new()
        {
            InvocationResponse = new()
            {
                InvocationId = invocationId,
                Result = new()
                {
                    Status = StatusResult.Types.Status.Failure,
                    Exception = new() { Message = sentinel, StackTrace = sentinel }
                },
                ReturnValue = new() { String = sentinel }
            }
        };
        await runtime.WriteAsync(request, timeout.Token);
        Assert.Equal(request, await worker.ReadAsync(timeout.Token));
        await worker.WriteAsync(response, timeout.Token);
        Assert.Equal(response, await runtime.ReadAsync(timeout.Token));
        using HttpClient http = factory.CreateHttpForwardingClient();
        using HttpRequestMessage httpRequest = new(HttpMethod.Get, $"/function?secret={sentinel}");
        httpRequest.Headers.Add("x-ms-invocation-id", invocationId);
        using HttpResponseMessage httpResponse = await http.SendAsync(httpRequest, timeout.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, httpResponse.StatusCode);
        await worker.CompleteRequestAsync(timeout.Token);
        await runtime.WaitForTerminationAsync(timeout.Token);
        await WaitForLogAsync(() => generator.ProxyRecords.Count(record =>
            string.Equals(record.EventName, "PeerReleased", StringComparison.Ordinal)) == 2, timeout.Token);
        Assert.Contains(generator.ProxyRecords, record =>
            string.Equals(record.EventName, "SessionTerminated", StringComparison.Ordinal)
            && string.Equals(record.Context?.WorkerId, "telemetry-worker", StringComparison.Ordinal)
            && record.Context?.Attributes?.WorkerReady is false);
        Assert.Equal(8, generator.ProxyRecords.Count(record =>
            string.Equals(record.EventName, "ProtocolMessageObserved", StringComparison.Ordinal)));
        Assert.DoesNotContain(generator.ProxyRecords, record =>
            string.Equals(record.Context?.Operation, "RpcInvocation", StringComparison.Ordinal)
            || string.Equals(record.Context?.Operation, "HttpForwarding", StringComparison.Ordinal));
        Assert.DoesNotContain(generator.ProxyRecords, record =>
            record.Message.Contains(sentinel, StringComparison.Ordinal)
            || (record.Context?.ToString().Contains(sentinel, StringComparison.Ordinal) ?? false));
    }

    private static async Task WaitForLogAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        while (!predicate())
        {
            await Task.Delay(10, cancellationToken);
        }
    }
}
