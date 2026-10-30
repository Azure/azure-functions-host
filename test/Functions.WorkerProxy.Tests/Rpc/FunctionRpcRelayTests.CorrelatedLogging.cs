// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.Rpc;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_LogsCorrelatedBoundariesWithoutInvocationPayload(bool enableInvocations)
    {
        const string sentinel = "customer-payload-secret";
        const string invocationId = "invocation-id";
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = new(
            new Dictionary<string, string?>
            {
                ["WorkerProxy:EnableInvocationSystemLogs"] = enableInvocations.ToString()
            },
            services =>
            {
                services.RemoveAll<IWorkerSystemEventGenerator>();
                services.AddSingleton<IWorkerSystemEventGenerator>(generator);
            });
        using CancellationTokenSource timeout = new(TestTimeout);
        string linkId = Guid.NewGuid().ToString("N");
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token,
            new Metadata { { FunctionRpcRelayService.LinkIdMetadataKey, Guid.NewGuid().ToString("N") } });
        StreamingMessage startStream = CreateStartStream();
        startStream.StartStream.WorkerId = "telemetry-worker";
        await worker.WriteAsync(startStream, timeout.Token);
        await WaitForLogAsync(() => generator.ProxyRecords.Any(record =>
            string.Equals(record.EventName, "StartStreamValidated", StringComparison.Ordinal)), timeout.Token);
        CapturedProxyLog preLink = Assert.Single(generator.ProxyRecords, record =>
            string.Equals(record.EventName, "StartStreamValidated", StringComparison.Ordinal));
        Assert.Null(preLink.Context!.LinkId);

        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token,
            new Metadata { { FunctionRpcRelayService.LinkIdMetadataKey, linkId } });
        await runtime.ReadAsync(timeout.Token);
        StreamingMessage init = new() { WorkerInitRequest = new() };
        await runtime.WriteAsync(init, timeout.Token);
        Assert.Equal(init, await worker.ReadAsync(timeout.Token));
        StreamingMessage initialized = CreateInitResponse("init", httpUri: null);
        await worker.WriteAsync(initialized, timeout.Token);
        await runtime.ReadAsync(timeout.Token);

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
        if (enableInvocations)
        {
            await WaitForLogAsync(() => generator.ProxyRecords.Count(record =>
                string.Equals(record.Context?.Operation, "RpcInvocation", StringComparison.Ordinal)) == 4, timeout.Token);
        }

        CapturedProxyLog[] invocationLogs = generator.ProxyRecords.Where(record =>
            string.Equals(record.Context?.Operation, "RpcInvocation", StringComparison.Ordinal)).ToArray();
        Assert.Equal(enableInvocations ? 4 : 0, invocationLogs.Length);
        Assert.All(invocationLogs, record =>
        {
            Assert.Equal(linkId, record.Context!.LinkId);
            Assert.Equal(invocationId, record.Context.InvocationId);
            Assert.Equal("telemetry-worker", record.Context.WorkerId);
            Assert.Equal(preLink.Context.ProxySessionId, record.Context.ProxySessionId);
            Assert.Null(record.Context.Outcome);
        });
        if (enableInvocations)
        {
            Assert.Equal(new[] { "RequestForwarded", "RequestReceived", "ResponseForwarded", "ResponseReceived" },
                invocationLogs.Select(record => record.Context!.Stage).Order(StringComparer.Ordinal));
            Assert.All(invocationLogs.Where(record => record.Context!.Stage!.StartsWith("Response", StringComparison.Ordinal)),
                record => Assert.Equal("Failure", record.Context!.Attributes!.RpcStatus));
        }

        using HttpClient http = factory.CreateHttpForwardingClient();
        using HttpRequestMessage httpRequest = new(HttpMethod.Get, $"/function?secret={sentinel}");
        httpRequest.Headers.Add("x-ms-invocation-id", invocationId);
        using HttpResponseMessage httpResponse = await http.SendAsync(httpRequest, timeout.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, httpResponse.StatusCode);
        CapturedProxyLog httpLog = Assert.Single(generator.ProxyRecords, record =>
            string.Equals(record.EventName, "HttpForwardingCompleted", StringComparison.Ordinal));
        Assert.Equal(invocationId, httpLog.Context!.InvocationId);
        Assert.Equal(linkId, httpLog.Context.LinkId);
        Assert.Equal("Rejected", httpLog.Context.Outcome);
        Assert.Equal("DestinationNotConfigured", httpLog.Context.ReasonCode);
        Assert.Equal(503, httpLog.Context.Attributes!.HttpStatusCode);

        await worker.CompleteRequestAsync(timeout.Token);
        await runtime.WaitForTerminationAsync(timeout.Token);
        await WaitForLogAsync(() => generator.ProxyRecords.Count(record =>
            string.Equals(record.EventName, "PeerReleased", StringComparison.Ordinal)) == 2, timeout.Token);
        Assert.Contains(generator.ProxyRecords, record =>
            string.Equals(record.EventName, "SessionTerminated", StringComparison.Ordinal)
            && string.Equals(record.Context?.LinkId, linkId, StringComparison.Ordinal)
            && record.Context?.Attributes?.WorkerReady is false);
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
