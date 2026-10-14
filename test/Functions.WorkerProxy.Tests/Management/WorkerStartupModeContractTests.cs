// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Management;

public class WorkerStartupModeContractTests
{
    private const string AssignmentPath = "/admin/worker/assignment";
    // The Legion BYOC producer sends this shape, including an empty directory and environment.
    // No new endpoint field is needed for compatibility with the existing assignment contract.
    private const string PreconfiguredAssignment = """
        {"functionAppName":"test-app","functionGroupName":"http","isAlwaysReady":false,
         "startupMode":"Preconfigured","environment":{},"functionAppDirectory":""}
        """;

    [Theory]
    [InlineData(null, "Required")]
    [InlineData("", "Required")]
    [InlineData("preconfigured", "InvalidValue")]
    [InlineData("Specializationrequired", "InvalidValue")]
    [InlineData("Preconfigured ", "InvalidValue")]
    [InlineData("Unknown", "InvalidValue")]
    public async Task ConfiguredPrivateDefaultDoesNotReplaceMissingOrInvalidWireModeAsync(string? mode, string code)
    {
        await using WorkerProxyWebApplicationFactory factory = new(configurationValues: new Dictionary<string, string?>
        {
            [$"{WorkerProxyOptions.SectionName}:DefaultStartupMode"] = "Preconfigured"
        });
        using HttpClient client = factory.CreateWorkerProxyClient();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.True(manager.OnWorkerAttached(1));
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        WorkerPodState before = manager.State;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        JsonObject body = JsonNode.Parse(PreconfiguredAssignment)!.AsObject();
        if (mode is null)
        {
            body.Remove("startupMode");
        }
        else
        {
            body["startupMode"] = mode;
        }

        using StringContent content = JsonBody(body.ToJsonString());
        using HttpResponseMessage response = await client.PutAsync(AssignmentPath, content, timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        JsonElement error = Assert.Single(errors.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal("startupMode", error.GetProperty("target").GetString());
        Assert.Same(before, manager.State);
        Assert.Null(manager.State.StartupMode);
    }

    [Theory]
    [InlineData("Preconfigured", "SpecializationRequired")]
    [InlineData("SpecializationRequired", "Preconfigured")]
    public async Task ExplicitSerializedModesRetainLegacyFieldsAndRetryIdentityAsync(string mode, string changedMode)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.True(manager.OnWorkerAttached(1));
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        JsonObject body = JsonNode.Parse(PreconfiguredAssignment)!.AsObject();
        body["startupMode"] = mode;
        // Both modes must have the same valid directory to isolate mode-only retry conflicts.
        body["functionAppDirectory"] = "/home/site/wwwroot";
        using StringContent content = JsonBody(body.ToJsonString());
        using HttpResponseMessage created = await client.PutAsync(AssignmentPath, content, timeout.Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        WorkerPodState assigned = manager.State;
        Assert.Equal(mode, assigned.StartupMode.ToString());
        Assert.Equal("test-app", assigned.FunctionAppName);
        Assert.Equal("http", assigned.FunctionGroupName);
        Assert.False(assigned.IsAlwaysReady);

        using StringContent retryContent = JsonBody(body.ToJsonString());
        using HttpResponseMessage retry = await client.PutAsync(AssignmentPath, retryContent, timeout.Token);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Same(assigned, manager.State);

        body["startupMode"] = changedMode;
        using StringContent changedContent = JsonBody(body.ToJsonString());
        using HttpResponseMessage changed = await client.PutAsync(AssignmentPath, changedContent, timeout.Token);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using JsonDocument conflict = JsonDocument.Parse(await changed.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal("AssignmentConflict", conflict.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Same(assigned, manager.State);
    }

    private static StringContent JsonBody(string body) => new(body, Encoding.UTF8, "application/json");

    [Theory]
    [InlineData("Preconfigured", HttpStatusCode.Created)]
    [InlineData("SpecializationRequired", HttpStatusCode.BadRequest)]
    public async Task SerializedByocDirectoryRulesRemainModeDependentAsync(string mode, HttpStatusCode expectedStatus)
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateWorkerProxyClient();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.True(manager.OnWorkerAttached(1));
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        WorkerPodState before = manager.State;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        JsonObject body = JsonNode.Parse(PreconfiguredAssignment)!.AsObject();
        body["startupMode"] = mode;
        using StringContent content = JsonBody(body.ToJsonString());
        using HttpResponseMessage response = await client.PutAsync(AssignmentPath, content, timeout.Token);
        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.BadRequest)
        {
            using JsonDocument errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            JsonElement error = Assert.Single(errors.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal("Required", error.GetProperty("code").GetString());
            Assert.Equal("functionAppDirectory", error.GetProperty("target").GetString());
            Assert.Same(before, manager.State);
        }
        else
        {
            Assert.Equal(WorkerStartupMode.Preconfigured, manager.State.StartupMode);
            Assert.Equal(WorkerAssignmentState.Ready, manager.State.AssignmentState);
        }
    }
}
