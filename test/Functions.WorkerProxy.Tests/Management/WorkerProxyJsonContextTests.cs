// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
            Environment = new Dictionary<string, string?> { ["MixedCase_SETTING"] = string.Empty, ["SECRET"] = "private-value" }
        };

        string json = JsonSerializer.Serialize(request, WorkerProxyJsonContext.Default.WorkerAssignRequest);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        AssertProperties(root, "startupMode", "functionAppName", "functionGroupName", "functionAppDirectory", "isAlwaysReady", "environment");
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
    public void InitialState_ContainsOnlyPublicShapeAndOmitsUnknownIdentity()
    {
        WorkerInstanceState response = WorkerInstanceState.FromState(WorkerPodState.CreateUnassigned("pod"));

        string json = JsonSerializer.Serialize(response, WorkerProxyJsonContext.Default.WorkerInstanceState);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        AssertProperties(root, "podName", "revisionId", "workerPodState", "functionsContainerType");
        Assert.Equal("pod", root.GetProperty("podName").GetString());
        Assert.Equal(0, root.GetProperty("revisionId").GetInt64());
        Assert.Equal("FunctionsWorkerPod", root.GetProperty("functionsContainerType").GetString());
        AssertProperties(root.GetProperty("workerPodState"), "podStatus");
        Assert.Equal("None", root.GetProperty("workerPodState").GetProperty("podStatus").GetString());
        Assert.Equal(response, JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerInstanceState));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AssignedState_ExposesOnlyPublicIdentityAndPreservesLongRevision(bool isAlwaysReady, bool specializationRequired)
    {
        WorkerStartupMode startupMode = specializationRequired ? WorkerStartupMode.SpecializationRequired : WorkerStartupMode.Preconfigured;
        WorkerPodState state = new(
            PodName: "pod",
            Revision: long.MaxValue,
            SessionId: 123,
            IsWorkerAttached: true,
            WorkerId: "private-worker",
            AssignmentState: WorkerAssignmentState.Ready,
            StartupMode: startupMode,
            FunctionAppName: "private-app",
            FunctionGroupName: "group",
            IsAlwaysReady: isAlwaysReady);
        WorkerInstanceState response = WorkerInstanceState.FromState(state);

        string json = JsonSerializer.Serialize(response, WorkerProxyJsonContext.Default.WorkerInstanceState);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement podState = root.GetProperty("workerPodState");

        AssertProperties(root, "podName", "revisionId", "workerPodState", "functionsContainerType");
        Assert.Equal("pod", root.GetProperty("podName").GetString());
        Assert.Equal("FunctionsWorkerPod", root.GetProperty("functionsContainerType").GetString());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("revisionId").ValueKind);
        Assert.Equal(long.MaxValue, root.GetProperty("revisionId").GetInt64());
        AssertProperties(podState, "podStatus", "startupMode", "functionGroupName", "isAlwaysReady");
        Assert.Equal(startupMode.ToString(), podState.GetProperty("startupMode").GetString());
        Assert.Equal("ReadyForRequest", podState.GetProperty("podStatus").GetString());
        Assert.Equal("group", podState.GetProperty("functionGroupName").GetString());
        Assert.Equal(isAlwaysReady, podState.GetProperty("isAlwaysReady").GetBoolean());
        Assert.DoesNotContain("private-worker", json);
        Assert.DoesNotContain("private-app", json);
        Assert.Equal(response, JsonSerializer.Deserialize(json, WorkerProxyJsonContext.Default.WorkerInstanceState));
    }

    [Fact]
    public void FromState_RetainedResponseDoesNotChangeWhenSourceStateIsReplaced()
    {
        WorkerPodState ready = new("pod", 3, 1, true, "worker", WorkerAssignmentState.Ready, WorkerStartupMode.Preconfigured, "app", "group", false);
        WorkerInstanceState retained = WorkerInstanceState.FromState(ready);
        WorkerPodState terminated = ready with
        {
            Revision = 4,
            IsWorkerAttached = false,
            AssignmentState = WorkerAssignmentState.Failed
        };

        string retainedJson = JsonSerializer.Serialize(retained, WorkerProxyJsonContext.Default.WorkerInstanceState);
        string terminatedJson = JsonSerializer.Serialize(
            WorkerInstanceState.FromState(terminated), WorkerProxyJsonContext.Default.WorkerInstanceState);
        using JsonDocument retainedDocument = JsonDocument.Parse(retainedJson);
        using JsonDocument terminatedDocument = JsonDocument.Parse(terminatedJson);

        Assert.Equal(3, retainedDocument.RootElement.GetProperty("revisionId").GetInt64());
        Assert.Equal("ReadyForRequest", retainedDocument.RootElement.GetProperty("workerPodState").GetProperty("podStatus").GetString());
        Assert.Equal(4, terminatedDocument.RootElement.GetProperty("revisionId").GetInt64());
        JsonElement terminatedPodState = terminatedDocument.RootElement.GetProperty("workerPodState");
        Assert.Equal("None", terminatedPodState.GetProperty("podStatus").GetString());
        Assert.Equal("Preconfigured", terminatedPodState.GetProperty("startupMode").GetString());
        Assert.Equal("group", terminatedPodState.GetProperty("functionGroupName").GetString());
        Assert.False(terminatedPodState.GetProperty("isAlwaysReady").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("The request is invalid.")]
    public void ErrorEnvelope_RoundTripsAndOmitsNullDetail(string? detail)
    {
        WorkerApiErrorResponse response = new(new WorkerApiError("WorkerNotReady", detail));

        string json = JsonSerializer.Serialize(response, WorkerProxyJsonContext.Default.WorkerApiErrorResponse);
        using JsonDocument document = JsonDocument.Parse(json);
        AssertProperties(document.RootElement, "error");
        JsonElement error = document.RootElement.GetProperty("error");
        Assert.Equal("WorkerNotReady", error.GetProperty("code").GetString());
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
        WorkerPodStateResponse state = new(WorkerPodStatus.ReadyForRequest, WorkerStartupMode.Preconfigured, "group", false);
        WorkerApiError error = new("WorkerNotReady");

        string stateJson = JsonSerializer.Serialize(state, WorkerProxyJsonContext.Default.WorkerPodStateResponse);
        string errorJson = JsonSerializer.Serialize(error, WorkerProxyJsonContext.Default.WorkerApiError);

        Assert.Equal(state, JsonSerializer.Deserialize(stateJson, WorkerProxyJsonContext.Default.WorkerPodStateResponse));
        Assert.Equal(error, JsonSerializer.Deserialize(errorJson, WorkerProxyJsonContext.Default.WorkerApiError));
        using JsonDocument stateDocument = JsonDocument.Parse(stateJson);
        Assert.Equal("ReadyForRequest", stateDocument.RootElement.GetProperty("podStatus").GetString());
        Assert.Equal("Preconfigured", stateDocument.RootElement.GetProperty("startupMode").GetString());
        Assert.False(stateDocument.RootElement.GetProperty("isAlwaysReady").GetBoolean());
    }

    private static void AssertProperties(JsonElement element, params string[] names) =>
        Assert.Equal(names.OrderBy(name => name), element.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
}
