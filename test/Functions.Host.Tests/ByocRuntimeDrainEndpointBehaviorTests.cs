// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
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
/// Exercises the hosted BYOC Runtime drain management contract.
/// </summary>
public sealed class ByocRuntimeDrainEndpointBehaviorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Post_EnforcesAdminAuthAndExactDrainModelContract()
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        DateTimeOffset acceptedAtUtc = new(2026, 10, 31, 7, 0, 0, TimeSpan.Zero);
        Mock<IByocRuntimeDrainManager> manager = new(MockBehavior.Strict);
        manager.Setup(value => value.Accept(ByocDrainModel.Phased))
            .Returns(new ByocRuntimeDrainReceipt(ByocDrainModel.Phased, acceptedAtUtc));
        manager.Setup(value => value.Accept(ByocDrainModel.Immediate))
            .Returns(new ByocRuntimeDrainReceipt(ByocDrainModel.Immediate, acceptedAtUtc));
        await using ByocRuntimeDrainTestHost host =
            await ByocRuntimeDrainTestHost.StartAsync(
                manager.Object,
                timeout.Token);

        using (HttpResponseMessage unauthorized = await host.PostAsync(
            """{"drainModel":"Phased"}""",
            authenticated: false,
            timeout.Token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }

        foreach (ByocDrainModel drainModel in Enum.GetValues<ByocDrainModel>())
        {
            using HttpResponseMessage response = await host.PostAsync(
                $$"""{"drainModel":"{{drainModel}}"}""",
                authenticated: true,
                timeout.Token);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            using JsonDocument document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(timeout.Token));
            Assert.Equal(
                drainModel.ToString(),
                document.RootElement.GetProperty("drainModel").GetString());
            Assert.Equal(
                acceptedAtUtc,
                document.RootElement.GetProperty("acceptedAtUtc").GetDateTimeOffset());
        }

        IReadOnlyList<(string? Body, string Code, string Target)> invalidRequests =
        [
            (null, "InvalidBody", "request"),
            (string.Empty, "InvalidBody", "request"),
            ("null", "InvalidBody", "request"),
            ("[]", "InvalidBody", "request"),
            ("{}", "Required", "drainModel"),
            ("""{"drainModel":null}""", "InvalidBody", "request"),
            ("""{"drainModel":123}""", "InvalidBody", "request"),
            ("""{"drainModel":""}""", "Required", "drainModel"),
            ("""{"drainModel":"phased"}""", "InvalidValue", "drainModel"),
            ("""{"drainModel":"Unknown"}""", "InvalidValue", "drainModel"),
            ("""{"DrainModel":"Phased"}""", "InvalidBody", "request"),
            ("""{"drainModel":"Phased","reason":"stop"}""", "InvalidBody", "request"),
            ("""{"drainModel":"Phased","drainModel":"Immediate"}""", "InvalidBody", "request"),
            ("""{"drainModel":"Phased","broken":}""", "InvalidBody", "request"),
        ];
        foreach ((string? body, string code, string target) in invalidRequests)
        {
            using HttpResponseMessage response = await host.PostAsync(
                body,
                authenticated: true,
                timeout.Token);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using JsonDocument document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(timeout.Token));
            JsonElement error = Assert.Single(
                document.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal(code, error.GetProperty("code").GetString());
            Assert.Equal(target, error.GetProperty("target").GetString());
        }

        manager.Verify(value => value.Accept(ByocDrainModel.Phased), Times.Once);
        manager.Verify(value => value.Accept(ByocDrainModel.Immediate), Times.Once);
        manager.VerifyNoOtherCalls();

        await using ByocRuntimeDrainTestHost standardHost =
            await ByocRuntimeDrainTestHost.StartAsync(
                manager.Object,
                timeout.Token,
                includeCompute: false);
        using HttpResponseMessage missingRoute = await standardHost.PostAsync(
            """{"drainModel":"Phased"}""",
            authenticated: true,
            timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, missingRoute.StatusCode);
    }
}
