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
/// Exercises BYOC Runtime drain behavior through the hosted management endpoint.
/// </summary>
public sealed class ByocRuntimeDrainEndpointBehaviorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Post_PropagatesEscalationReceiptAndConflict()
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        DateTimeOffset acceptedAtUtc =
            new(2026, 10, 31, 7, 0, 0, TimeSpan.Zero);
        Mock<IByocRuntimeDrainManager> manager = new(MockBehavior.Strict);
        manager.SetupSequence(
                value => value.Accept(ByocDrainModel.Phased))
            .Returns(new ByocRuntimeDrainReceipt(
                ByocDrainModel.Phased,
                acceptedAtUtc))
            .Throws(new ByocRuntimeDrainConflictException(
                "The Runtime is already draining with the Immediate model."));
        manager.Setup(
                value => value.Accept(ByocDrainModel.Immediate))
            .Returns(new ByocRuntimeDrainReceipt(
                ByocDrainModel.Immediate,
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
            (ByocDrainModel.Phased, acceptedAtUtc),
            await ReadReceiptAsync(phased, timeout.Token));

        using HttpResponseMessage immediate = await host.PostDrainAsync(
            """{"drainMode":"Immediate"}""",
            timeout.Token);
        Assert.Equal(
            (ByocDrainModel.Immediate, acceptedAtUtc),
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

    [Fact]
    public async Task Post_RejectsInvalidRequests()
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        Mock<IByocRuntimeDrainManager> manager = new(MockBehavior.Strict);
        await using WorkerLinkTestHost host =
            await WorkerLinkTestHost.StartAsync(
                timeout.Token,
                Mock.Of<IWorkerChannelRegistry>(),
                drainManager: manager.Object);

        await AssertValidationErrorAsync(
            host,
            string.Empty,
            "InvalidBody",
            "request",
            timeout.Token);
        await AssertValidationErrorAsync(
            host,
            "{}",
            "Required",
            "drainMode",
            timeout.Token);
        await AssertValidationErrorAsync(
            host,
            """{"drainMode":"phased"}""",
            "InvalidValue",
            "drainMode",
            timeout.Token);
        await AssertValidationErrorAsync(
            host,
            """{"drainMode":"Phased","reason":"stop"}""",
            "InvalidBody",
            "request",
            timeout.Token);

        manager.VerifyNoOtherCalls();
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

    private static async Task<(ByocDrainModel DrainModel, DateTimeOffset AcceptedAtUtc)> ReadReceiptAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return (
            Enum.Parse<ByocDrainModel>(
                document.RootElement
                    .GetProperty("drainMode")
                    .GetString()!),
            document.RootElement
                .GetProperty("acceptedAtUtc")
                .GetDateTimeOffset());
    }
}
