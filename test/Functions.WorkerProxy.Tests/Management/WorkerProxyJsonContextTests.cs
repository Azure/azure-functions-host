// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.State;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Management;

public class WorkerProxyJsonContextTests
{
    [Theory]
    [InlineData("Preconfigured")]
    [InlineData("SpecializationRequired")]
    public void Assignment_RoundTripsWithCamelCaseAndExplicitFalse(string startupMode)
    {
        WorkerAssignRequest request = new()
        {
            StartupMode = startupMode,
            FunctionAppName = "app",
            FunctionGroupName = "group",
            FunctionAppDirectory = "private-directory",
            IsAlwaysReady = false,
            Environment = new Dictionary<string, string?> { ["MixedCase_SETTING"] = string.Empty, ["SECRET"] = "private-value" },
            HttpProxyEndpoint = "127.0.0.1:48801"
        };

        string json = JsonSerializer.Serialize(request, WorkerProxyJsonContext.Default.WorkerAssignRequest);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        AssertProperties(root, "startupMode", "functionAppName", "functionGroupName", "functionAppDirectory", "isAlwaysReady", "environment", "httpProxyEndpoint");
        Assert.Equal(startupMode, root.GetProperty("startupMode").GetString());
        Assert.False(root.GetProperty("isAlwaysReady").GetBoolean());
        Assert.Equal(string.Empty, root.GetProperty("environment").GetProperty("MixedCase_SETTING").GetString());
        Assert.Equal("private-value", root.GetProperty("environment").GetProperty("SECRET").GetString());

        WorkerAssignRequest copy = Assert.IsType<WorkerAssignRequest>(
            JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerAssignRequest));
        Assert.Equal(request.StartupMode, copy.StartupMode);
        Assert.Equal(request.FunctionAppName, copy.FunctionAppName);
        Assert.Equal(request.FunctionGroupName, copy.FunctionGroupName);
        Assert.Equal(request.FunctionAppDirectory, copy.FunctionAppDirectory);
        Assert.Equal(request.HttpProxyEndpoint, copy.HttpProxyEndpoint);
        Assert.False(copy.IsAlwaysReady);
        Assert.Equal(request.Environment.OrderBy(pair => pair.Key), copy.Environment!.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void Assignment_PropertyNamesAreCaseInsensitiveButEnvironmentKeysArePreserved()
    {
        const string json = """
            {"STARTUPMODE":"Preconfigured","FUNCTIONAPPNAME":"app","FunctionGroupName":"group","FUNCTIONAPPDIRECTORY":"directory",
             "ISALWAYSREADY":true,"ENVIRONMENT":{"Key":"one","key":"two"}}
            """;

        WorkerAssignRequest request = Assert.IsType<WorkerAssignRequest>(
            JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerAssignRequest));

        Assert.Equal("Preconfigured", request.StartupMode);
        Assert.Equal("app", request.FunctionAppName);
        Assert.Equal("group", request.FunctionGroupName);
        Assert.Equal("directory", request.FunctionAppDirectory);
        Assert.True(request.IsAlwaysReady);
        Assert.Equal(2, request.Environment!.Count);
        Assert.Equal("one", request.Environment["Key"]);
        Assert.Equal("two", request.Environment["key"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"startupMode":null,"functionAppName":null,"functionGroupName":null,"functionAppDirectory":null,"isAlwaysReady":null,"environment":null}""")]
    public void Assignment_MissingRequiredValuesAreLeftForHandlerValidation(string json)
    {
        WorkerAssignRequest request = Assert.IsType<WorkerAssignRequest>(
            JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerAssignRequest));

        Assert.Null(request.StartupMode);
        Assert.Null(request.FunctionAppName);
        Assert.Null(request.FunctionGroupName);
        Assert.Null(request.FunctionAppDirectory);
        Assert.Null(request.IsAlwaysReady);
        Assert.Null(request.Environment);
        Assert.Equal("{}", JsonSerializer.Serialize(request, WorkerProxyJsonContext.Default.WorkerAssignRequest));
    }

    [Fact]
    public void Assignment_NullEnvironmentValueIsLeftForHandlerValidation()
    {
        WorkerAssignRequest request = Assert.IsType<WorkerAssignRequest>(JsonSerializer.Deserialize(
            """{"environment":{"SETTING":null}}""", WorkerProxyJsonContext.Default.WorkerAssignRequest));

        Assert.True(request.Environment!.ContainsKey("SETTING"));
        Assert.Null(request.Environment["SETTING"]);
    }

    [Theory]
    [InlineData("""{"startupMode":0}""")]
    [InlineData("""{"startupMode":true}""")]
    [InlineData("""{"startupMode":[]}""")]
    [InlineData("""{"startupMode":{}}""")]
    [InlineData("""{"isAlwaysReady":"false"}""")]
    [InlineData("""{"isAlwaysReady":0}""")]
    [InlineData("""{"functionAppName":123}""")]
    [InlineData("""{"functionGroupName":[]}""")]
    [InlineData("""{"functionAppDirectory":{}}""")]
    [InlineData("""{"environment":[]}""")]
    [InlineData("""{"environment":{"SETTING":123}}""")]
    [InlineData("[]")]
    [InlineData("{")]
    public void Assignment_MalformedTypesThrowJsonException(string json)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerAssignRequest));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("preconfigured")]
    [InlineData("Unknown")]
    [InlineData("0")]
    public void Assignment_InvalidModeStringsArePreservedForFieldValidation(string startupMode)
    {
        string json = $$"""{"startupMode":"{{startupMode}}"}""";

        WorkerAssignRequest request = Assert.IsType<WorkerAssignRequest>(
            JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerAssignRequest));

        Assert.Equal(startupMode, request.StartupMode);
    }

    [Fact]
    public void NullAssignment_DeserializesAsNullForHandlerValidation()
    {
        Assert.Null(JsonSerializer.Deserialize("null", WorkerProxyJsonContext.Default.WorkerAssignRequest));
    }

    [Fact]
    public void OriginState_UsesLiteralFlatDiscriminatorFirstShapeWithoutFabricatingPlatformMetadata()
    {
        DateTimeOffset startup = DateTimeOffset.Parse("2026-10-29T23:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);
        WorkerPodState assigned = WorkerPodState.CreateUnassigned("private-pod") with
        {
            Revision = 1,
            AssignmentState = WorkerAssignmentState.Ready,
            FunctionAppName = "private-app",
            FunctionGroupName = "http",
            IsAlwaysReady = false
        };
        WorkerInstanceState response = WorkerInstanceState.FromState(assigned, startup);

        string json = JsonSerializer.Serialize(response, WorkerProxyJsonContext.Default.WorkerInstanceState);
        const string expected = """
            {"functionsContainerType":"FunctionsSplitWorkerProxy","isDiscoveryPod":false,"isAlwaysReady":false,"functionGroup":"http","startupTime":"2026-10-29T23:00:00+00:00","revisionId":1,"status":"Starting","link":{"state":"Unlinked","runtime":null},"isUnhealthy":false}
            """;
        Assert.Equal(expected, json);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("functionsContainerType", root.EnumerateObject().First().Name);
        Assert.False(root.TryGetProperty("self", out _));
        Assert.False(root.TryGetProperty("configLastModifiedTime", out _));
        Assert.False(root.TryGetProperty("contentLastModifiedTime", out _));
        Assert.DoesNotContain("private-", json, StringComparison.Ordinal);
        Assert.Equal(response, JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerInstanceState));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AssignedState_UsesProducerRevisionRangeAndDoesNotClaimAcceptedLink(bool isAlwaysReady, bool specializationRequired)
    {
        WorkerStartupMode startupMode = specializationRequired ? WorkerStartupMode.SpecializationRequired : WorkerStartupMode.Preconfigured;
        WorkerPodState state = new(
            PodName: "pod",
            Revision: int.MaxValue,
            SessionId: 123,
            IsWorkerAttached: true,
            WorkerId: "private-worker",
            AssignmentState: WorkerAssignmentState.Ready,
            StartupMode: startupMode,
            FunctionAppName: "private-app",
            FunctionGroupName: "group",
            IsAlwaysReady: isAlwaysReady);
        WorkerInstanceState response = WorkerInstanceState.FromState(state, DateTimeOffset.UtcNow);

        string json = JsonSerializer.Serialize(response, WorkerProxyJsonContext.Default.WorkerInstanceState);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("FunctionsSplitWorkerProxy", root.GetProperty("functionsContainerType").GetString());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("revisionId").ValueKind);
        Assert.Equal(int.MaxValue, root.GetProperty("revisionId").GetInt32());
        Assert.Equal("Starting", root.GetProperty("status").GetString());
        Assert.Equal("Unlinked", root.GetProperty("link").GetProperty("state").GetString());
        Assert.Equal("group", root.GetProperty("functionGroup").GetString());
        Assert.Equal(isAlwaysReady, root.GetProperty("isAlwaysReady").GetBoolean());
        Assert.DoesNotContain("private-worker", json);
        Assert.DoesNotContain("private-app", json);
        Assert.Equal(response, JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerInstanceState));
    }

    [Fact]
    public void FromState_RetainedResponseDoesNotChangeWhenSourceStateIsReplaced()
    {
        WorkerPodState ready = new("pod", 3, 1, true, "worker", WorkerAssignmentState.Ready, WorkerStartupMode.Preconfigured, "app", "group", false);
        DateTimeOffset startup = DateTimeOffset.UtcNow;
        WorkerInstanceState retained = WorkerInstanceState.FromState(ready, startup);
        WorkerPodState terminated = ready with
        {
            Revision = 4,
            IsWorkerAttached = false,
            AssignmentState = WorkerAssignmentState.Failed
        };

        string retainedJson = JsonSerializer.Serialize(retained, WorkerProxyJsonContext.Default.WorkerInstanceState);
        string terminatedJson = JsonSerializer.Serialize(
            WorkerInstanceState.FromState(terminated, startup), WorkerProxyJsonContext.Default.WorkerInstanceState);
        using JsonDocument retainedDocument = JsonDocument.Parse(retainedJson);
        using JsonDocument terminatedDocument = JsonDocument.Parse(terminatedJson);

        Assert.Equal(3, retainedDocument.RootElement.GetProperty("revisionId").GetInt64());
        Assert.False(retainedDocument.RootElement.GetProperty("isUnhealthy").GetBoolean());
        Assert.Equal(4, terminatedDocument.RootElement.GetProperty("revisionId").GetInt64());
        Assert.True(terminatedDocument.RootElement.GetProperty("isUnhealthy").GetBoolean());
        Assert.Equal("Starting", terminatedDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal("group", terminatedDocument.RootElement.GetProperty("functionGroup").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("The request is invalid.")]
    public void ErrorEnvelope_RoundTripsAndOmitsNullDetail(string? detail)
    {
        WorkerApiErrorResponse response = new(new WorkerApiError("AssignmentConflict", detail));

        string json = JsonSerializer.Serialize(response, WorkerProxyJsonContext.Default.WorkerApiErrorResponse);
        using JsonDocument document = JsonDocument.Parse(json);
        AssertProperties(document.RootElement, "error");
        JsonElement error = document.RootElement.GetProperty("error");
        Assert.Equal("AssignmentConflict", error.GetProperty("code").GetString());
        if (detail is null)
        {
            AssertProperties(error, "code");
        }
        else
        {
            AssertProperties(error, "code", "detail");
            Assert.Equal(detail, error.GetProperty("detail").GetString());
        }

        Assert.Equal(response, JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerApiErrorResponse));
    }

    [Fact]
    public void ValidationEnvelope_RoundTripsAllFieldErrorsUsingGeneratedMetadata()
    {
        RequestValidationError[] details =
        [
            new("Required", "functionAppName"),
            new("Required", "functionGroupName"),
            new("InvalidValue", "environment")
        ];
        RequestValidationResponse response = new(details);

        string json = JsonSerializer.Serialize(response, WorkerProxyJsonContext.Default.RequestValidationResponse);
        using JsonDocument document = JsonDocument.Parse(json);
        AssertProperties(document.RootElement, "errors");
        JsonElement fields = document.RootElement.GetProperty("errors");
        Assert.Equal(details.Length, fields.GetArrayLength());
        for (int index = 0; index < details.Length; index++)
        {
            AssertProperties(fields[index], "code", "target");
            Assert.Equal(details[index].Code, fields[index].GetProperty("code").GetString());
            Assert.Equal(details[index].Target, fields[index].GetProperty("target").GetString());
        }

        RequestValidationResponse restored = Assert.IsType<RequestValidationResponse>(
            JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.RequestValidationResponse));
        Assert.Equal<RequestValidationError>(details, restored.Errors);
    }

    [Fact]
    public void Context_RecursivelyGeneratesMetadataForNestedResponseTypes()
    {
        WorkerLinkState state = new("Unlinked");
        WorkerApiError error = new("AssignmentConflict");

        string stateJson = JsonSerializer.Serialize(state, WorkerProxyJsonContext.Default.WorkerLinkState);
        string errorJson = JsonSerializer.Serialize(error, WorkerProxyJsonContext.Default.WorkerApiError);

        Assert.Equal(state, JsonSerializer.Deserialize(stateJson, WorkerProxyJsonContext.Default.WorkerLinkState));
        Assert.Equal(error, JsonSerializer.Deserialize(errorJson, WorkerProxyJsonContext.Default.WorkerApiError));
        using JsonDocument stateDocument = JsonDocument.Parse(stateJson);
        Assert.Equal("""{"state":"Unlinked","runtime":null}""", stateJson);
    }

    private static void AssertProperties(JsonElement element, params string[] names) =>
        Assert.Equal(names.OrderBy(name => name), element.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
}
