// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Moq;
using Xunit;

namespace Azure.Functions.Host.Tests;

/// <summary>
/// Exercises Runtime drain behavior through the hosted management endpoint.
/// </summary>
public sealed class RuntimeDrainEndpointBehaviorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Post_PropagatesEscalationReceiptAndConflict()
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        DateTimeOffset acceptedAtUtc =
            new(2026, 10, 31, 7, 0, 0, TimeSpan.Zero);
        Mock<IRuntimeDrainManager> manager = new(MockBehavior.Strict);
        manager.SetupSequence(
                value => value.Accept(RuntimeDrainMode.Phased))
            .Returns(new RuntimeDrainReceipt(
                RuntimeDrainMode.Phased,
                acceptedAtUtc))
            .Throws(new RuntimeDrainConflictException(
                "The Runtime is already draining with the Immediate mode."));
        manager.Setup(
                value => value.Accept(RuntimeDrainMode.Immediate))
            .Returns(new RuntimeDrainReceipt(
                RuntimeDrainMode.Immediate,
                acceptedAtUtc));
        await using WorkerLinkTestHost host =
            await WorkerLinkTestHost.StartAsync(
                timeout.Token,
                Mock.Of<IWorkerChannelRegistry>(),
                drainManager: manager.Object);

        using HttpResponseMessage phased = await host.PostDrainAsync(
            """{"drainMode":"Phased"}""",
            timeout.Token);
        Assert.Equal(
            (RuntimeDrainMode.Phased, acceptedAtUtc),
            await ReadReceiptAsync(phased, timeout.Token));

        using HttpResponseMessage immediate = await host.PostDrainAsync(
            """{"drainMode":"Immediate"}""",
            timeout.Token);
        Assert.Equal(
            (RuntimeDrainMode.Immediate, acceptedAtUtc),
            await ReadReceiptAsync(immediate, timeout.Token));

        using HttpResponseMessage downgrade = await host.PostDrainAsync(
            """{"drainMode":"Phased"}""",
            timeout.Token);
        Assert.Equal(HttpStatusCode.Conflict, downgrade.StatusCode);
        using JsonDocument conflict = JsonDocument.Parse(
            await downgrade.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(
            "DrainConflict",
            conflict.RootElement
                .GetProperty("error")
                .GetProperty("code")
                .GetString());
    }

    [Theory]
    [InlineData("", "InvalidBody", "request")]
    [InlineData("null", "InvalidBody", "request")]
    [InlineData("[]", "InvalidBody", "request")]
    [InlineData("true", "InvalidBody", "request")]
    [InlineData("\"Phased\"", "InvalidBody", "request")]
    [InlineData("{}", "Required", "drainMode")]
    [InlineData("""{"drainMode":""}""", "Required", "drainMode")]
    [InlineData("""{"drainMode":" \t"}""", "Required", "drainMode")]
    [InlineData("""{"drainMode":"phased"}""", "InvalidValue", "drainMode")]
    [InlineData("""{"drainMode":"IMMEDIATE"}""", "InvalidValue", "drainMode")]
    [InlineData("""{"drainMode":"0"}""", "InvalidValue", "drainMode")]
    [InlineData("""{"drainMode":"1"}""", "InvalidValue", "drainMode")]
    [InlineData("""{"drainMode":"Phased, Immediate"}""", "InvalidValue", "drainMode")]
    [InlineData("""{"drainMode":"Immediate "}""", "InvalidValue", "drainMode")]
    [InlineData("""{"drainMode":"Phased","reason":"stop"}""", "InvalidBody", "request")]
    [InlineData("""{"DrainMode":"Phased"}""", "InvalidBody", "request")]
    [InlineData("""{"drainMode":"Phased","DrainMode":"Immediate"}""", "InvalidBody", "request")]
    [InlineData("""{"drainMode":"Phased","drain\u004dode":"Immediate"}""", "InvalidBody", "request")]
    [InlineData("""{"drainMode":"Phased","drainMode":"Immediate"}""", "InvalidBody", "request")]
    [InlineData("""{"drainMode":null}""", "InvalidBody", "request")]
    [InlineData("""{"drainMode":1}""", "InvalidBody", "request")]
    [InlineData("""{"drainMode":true}""", "InvalidBody", "request")]
    [InlineData("""{"drainMode":[]}""", "InvalidBody", "request")]
    [InlineData("""{"drainMode":{}}""", "InvalidBody", "request")]
    public async Task Post_RejectsInvalidRequests(string body, string expectedCode, string expectedTarget)
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        Mock<IRuntimeDrainManager> manager = new(MockBehavior.Strict);
        await using WorkerLinkTestHost host =
            await WorkerLinkTestHost.StartAsync(
                timeout.Token,
                Mock.Of<IWorkerChannelRegistry>(),
                drainManager: manager.Object);

        await AssertValidationErrorAsync(host, body, expectedCode, expectedTarget, timeout.Token);

        manager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Post_WithoutComputeComposition_DoesNotExposeDrainRoute()
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        Mock<IRuntimeDrainManager> manager = new(MockBehavior.Strict);
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(
            timeout.Token, Mock.Of<IWorkerChannelRegistry>(), includeCompute: false, drainManager: manager.Object);

        using HttpResponseMessage response = await host.PostDrainAsync("""{"drainMode":"Immediate"}""", timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        manager.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(RuntimeDrainMode.Phased)]
    [InlineData(RuntimeDrainMode.Immediate)]
    public async Task Post_FailedDrainDoesNotReturnAcceptance(RuntimeDrainMode drainMode)
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        Mock<IRuntimeDrainManager> manager = new(MockBehavior.Strict);
        manager.Setup(value => value.Accept(drainMode))
            .Throws(new InvalidOperationException("The Runtime drain previously failed."));
        await using WorkerLinkTestHost host = await WorkerLinkTestHost.StartAsync(
            timeout.Token, Mock.Of<IWorkerChannelRegistry>(), useKestrel: true, drainManager: manager.Object);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage response = await host.PostDrainAsync(
                $$"""{"drainMode":"{{drainMode}}"}""", timeout.Token);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        manager.Verify(value => value.Accept(drainMode), Times.Exactly(2));
    }

    private static async Task AssertValidationErrorAsync(
        WorkerLinkTestHost host,
        string body,
        string expectedCode,
        string expectedTarget,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await host.PostDrainAsync(
            body,
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        JsonElement error = Assert.Single(
            document.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal(
            expectedCode,
            error.GetProperty("code").GetString());
        Assert.Equal(
            expectedTarget,
            error.GetProperty("target").GetString());
    }

    private static async Task<(RuntimeDrainMode DrainMode, DateTimeOffset AcceptedAtUtc)> ReadReceiptAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return (
            Enum.Parse<RuntimeDrainMode>(
                document.RootElement
                    .GetProperty("drainMode")
                    .GetString()!),
            document.RootElement
                .GetProperty("acceptedAtUtc")
                .GetDateTimeOffset());
    }
}
