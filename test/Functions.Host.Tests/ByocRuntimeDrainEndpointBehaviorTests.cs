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

    [Theory]
    [InlineData(ByocDrainModel.Phased)]
    [InlineData(ByocDrainModel.Immediate)]
    public async Task Post_AcceptsSupportedDrainModel(
        ByocDrainModel drainModel)
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        DateTimeOffset acceptedAtUtc =
            new(2026, 10, 31, 7, 0, 0, TimeSpan.Zero);
        Mock<IByocRuntimeDrainManager> manager = new(MockBehavior.Strict);
        manager.Setup(value => value.Accept(drainModel))
            .Returns(new ByocRuntimeDrainReceipt(
                drainModel,
                acceptedAtUtc));
        await using ByocRuntimeDrainTestHost host =
            await ByocRuntimeDrainTestHost.StartAsync(
                manager.Object,
                timeout.Token);

        using HttpResponseMessage response = await host.PostAsync(
            $$"""{"drainModel":"{{drainModel}}"}""",
            authenticated: true,
            timeout.Token);

        Assert.Equal(
            (drainModel, acceptedAtUtc),
            await ReadReceiptAsync(response, timeout.Token));
        manager.Verify(value => value.Accept(drainModel), Times.Once);
        manager.VerifyNoOtherCalls();
    }

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
        await using ByocRuntimeDrainTestHost host =
            await ByocRuntimeDrainTestHost.StartAsync(
                manager.Object,
                timeout.Token);

        using HttpResponseMessage phased = await host.PostAsync(
            """{"drainModel":"Phased"}""",
            authenticated: true,
            timeout.Token);
        Assert.Equal(
            (ByocDrainModel.Phased, acceptedAtUtc),
            await ReadReceiptAsync(phased, timeout.Token));

        using HttpResponseMessage immediate = await host.PostAsync(
            """{"drainModel":"Immediate"}""",
            authenticated: true,
            timeout.Token);
        Assert.Equal(
            (ByocDrainModel.Immediate, acceptedAtUtc),
            await ReadReceiptAsync(immediate, timeout.Token));

        using HttpResponseMessage downgrade = await host.PostAsync(
            """{"drainModel":"Phased"}""",
            authenticated: true,
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
    public async Task Post_RejectsUnauthorizedAndInvalidRequests()
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        Mock<IByocRuntimeDrainManager> manager = new(MockBehavior.Strict);
        await using ByocRuntimeDrainTestHost host =
            await ByocRuntimeDrainTestHost.StartAsync(
                manager.Object,
                timeout.Token);

        using (HttpResponseMessage unauthorized = await host.PostAsync(
            """{"drainModel":"Phased"}""",
            authenticated: false,
            timeout.Token))
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                unauthorized.StatusCode);
        }

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
            "drainModel",
            timeout.Token);
        await AssertValidationErrorAsync(
            host,
            """{"drainModel":"phased"}""",
            "InvalidValue",
            "drainModel",
            timeout.Token);
        await AssertValidationErrorAsync(
            host,
            """{"drainModel":"Phased","reason":"stop"}""",
            "InvalidBody",
            "request",
            timeout.Token);

        manager.VerifyNoOtherCalls();
    }

    private static async Task AssertValidationErrorAsync(
        ByocRuntimeDrainTestHost host,
        string body,
        string expectedCode,
        string expectedTarget,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await host.PostAsync(
            body,
            authenticated: true,
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
                    .GetProperty("drainModel")
                    .GetString()!),
            document.RootElement
                .GetProperty("acceptedAtUtc")
                .GetDateTimeOffset());
    }
}
