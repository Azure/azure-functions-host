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
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Theory]
    [InlineData(0, StatusResult.Types.Status.Success)]
    [InlineData(500, StatusResult.Types.Status.Success)]
    [InlineData(3, StatusResult.Types.Status.Failure)]
    [InlineData(3, StatusResult.Types.Status.Cancelled)]
    [InlineData(3, (StatusResult.Types.Status)123)]
    [InlineData(3, null)]
    public async Task Relay_LogsBatchLoadBoundariesWithoutPerFunctionRecords(int count, StatusResult.Types.Status? lastStatus)
    {
        const string sentinel = "private-function-content";
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(generator);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        StreamingMessage startStream = CreateStartStream();
        startStream.StartStream.WorkerId = "batch-worker";
        await worker.WriteAsync(startStream, timeout.Token);
        await runtime.ReadAsync(timeout.Token);
        StreamingMessage request = new() { FunctionLoadRequestCollection = new() };
        StreamingMessage response = new() { FunctionLoadResponseCollection = new() };
        for (int index = 0; index < count; index++)
        {
            request.FunctionLoadRequestCollection.FunctionLoadRequests.Add(new FunctionLoadRequest { FunctionId = sentinel });
            StatusResult.Types.Status? status = index == count - 1 ? lastStatus : StatusResult.Types.Status.Success;
            response.FunctionLoadResponseCollection.FunctionLoadResponses.Add(new FunctionLoadResponse
            {
                FunctionId = sentinel,
                Result = status is { } resultStatus ? new StatusResult
                {
                    Status = resultStatus,
                    Exception = new() { Message = sentinel, StackTrace = sentinel }
                }
                : null
            });
        }

        await runtime.WriteAsync(request, timeout.Token);
        Assert.Equal(request, await worker.ReadAsync(timeout.Token));
        await worker.WriteAsync(response, timeout.Token);
        Assert.Equal(response, await runtime.ReadAsync(timeout.Token));
        await WaitForLogAsync(() => generator.ProxyRecords.Count(record =>
            record.EventName is "FunctionLoadBatchRequested" or "FunctionLoadBatchCompleted") == 4, timeout.Token);

        CapturedProxyLog[] records = generator.ProxyRecords.Where(record =>
            record.EventName is "FunctionLoadBatchRequested" or "FunctionLoadBatchCompleted").ToArray();
        int unsuccessful = count > 0 && lastStatus is not StatusResult.Types.Status.Success ? 1 : 0;
        foreach (string stage in new[] { "RequestReceived", "RequestForwarded", "ResponseReceived", "ResponseForwarded" })
        {
            bool isResponse = stage.StartsWith("Response", StringComparison.Ordinal);
            string message = isResponse
                ? $"FunctionRpc FunctionLoad batch reached {stage} with {count} results; {unsuccessful} unsuccessful or missing statuses."
                : $"FunctionRpc FunctionLoad batch reached {stage} with {count} functions.";
            CapturedProxyLog record = Assert.Single(records, record => string.Equals(record.Message, message, StringComparison.Ordinal));
            Assert.Equal(isResponse && unsuccessful > 0 ? LogLevel.Warning : LogLevel.Information, record.Level);
            Assert.Equal("batch-worker", record.Context!.WorkerId);
            Assert.Equal(records[0].Context!.ProxySessionId, record.Context.ProxySessionId);
        }

        Assert.DoesNotContain(generator.ProxyRecords, record => record.ToString().Contains(sentinel, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(StatusResult.Types.Status.Failure)]
    [InlineData(StatusResult.Types.Status.Success)]
    [InlineData(StatusResult.Types.Status.Cancelled)]
    [InlineData((StatusResult.Types.Status)123)]
    [InlineData(null)]
    public async Task Relay_LogsLinkAndStartupBoundariesWithoutInvocationTelemetry(StatusResult.Types.Status? loadStatus)
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
                Result = loadStatus is { } responseStatus ? new()
                {
                    Status = responseStatus,
                    Exception = new() { Message = sentinel, StackTrace = sentinel }
                }
                : null
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
                string.Equals(record.EventName, "ProtocolMessageObserved", StringComparison.Ordinal)
                && record.Message.StartsWith($"FunctionRpc {operation} reached ", StringComparison.Ordinal)).ToArray();
            Assert.Equal(4, records.Length);
            Assert.All(records, record =>
            {
                Assert.Equal("telemetry-worker", record.Context!.WorkerId);
                Assert.Equal(preLink.Context.ProxySessionId, record.Context.ProxySessionId);
            });
            foreach (string stage in new[] { "RequestForwarded", "RequestReceived", "ResponseForwarded", "ResponseReceived" })
            {
                StatusResult.Types.Status? status = stage.StartsWith("Response", StringComparison.Ordinal)
                    ? string.Equals(operation, "Initialization", StringComparison.Ordinal) ? StatusResult.Types.Status.Success : loadStatus
                    : null;
                string? statusText = status?.ToString();
                CapturedProxyLog record = Assert.Single(records, record =>
                    string.Equals(record.Message, $"FunctionRpc {operation} reached {stage} with RPC status {statusText ?? "(null)"}.",
                        StringComparison.Ordinal));
                Assert.Equal(status is not null and not StatusResult.Types.Status.Success ? LogLevel.Warning : LogLevel.Information,
                    record.Level);
            }
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
            && record.Context?.ProxySessionId == preLink.Context.ProxySessionId);
        Assert.Equal(8, generator.ProxyRecords.Count(record =>
            string.Equals(record.EventName, "ProtocolMessageObserved", StringComparison.Ordinal)));
        Assert.DoesNotContain(generator.ProxyRecords, record =>
            record.Message.Contains(invocationId, StringComparison.Ordinal));
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
