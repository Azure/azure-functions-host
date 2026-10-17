// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Azure.Functions.WorkerProxy.State;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Validates bound assignment requests and captures immutable identity without accessing worker state.
/// </summary>
internal static class WorkerAssignRequestValidator
{
    private const string RuntimePodType = "FunctionsSplitRuntimePod";
    private const string WorkerPodType = "FunctionsSplitWorkerPod";

    /// <summary>
    /// Creates an assignment from a valid request, or collects ordered field errors without exposing environment entries.
    /// </summary>
    public static bool TryCreateAssignment(
        WorkerAssignRequest request,
        string expectedPodName,
        string? workerContainerEncryptionKey,
        [NotNullWhen(true)] out WorkerAssignment? assignment,
        out IReadOnlyList<RequestValidationError> validationErrors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPodName);

        assignment = null;
        List<RequestValidationError> errors = [];
        validationErrors = errors;
        WorkerStartupMode? startupMode = ValidateStartupMode(request.StartupMode, errors);

        if (string.IsNullOrWhiteSpace(request.FunctionAppName))
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "functionAppName"));
        }

        if (string.IsNullOrWhiteSpace(request.FunctionGroupName))
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "functionGroupName"));
        }

        if (request.IsAlwaysReady is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "isAlwaysReady"));
        }

        if (request.FunctionAppDirectory is null
            || (startupMode is WorkerStartupMode.SpecializationRequired && string.IsNullOrWhiteSpace(request.FunctionAppDirectory)))
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "functionAppDirectory"));
        }

        Dictionary<string, string> environment = ValidateAndCopyEnvironment(request.Environment, errors);
        ValidateSplitState(
            request,
            expectedPodName,
            workerContainerEncryptionKey,
            errors,
            out SplitWorkerStaticState? splitStaticState,
            out SplitWorkerLink? splitLink);

        if (errors.Count > 0
            || startupMode is not { } selectedStartupMode
            || request.FunctionAppName is not { } functionAppName
            || request.FunctionGroupName is not { } functionGroupName
            || request.FunctionAppDirectory is not { } functionAppDirectory
            || request.IsAlwaysReady is not { } isAlwaysReady)
        {
            return false;
        }

        assignment = new(
            selectedStartupMode,
            functionAppName,
            functionGroupName,
            isAlwaysReady,
            environment,
            functionAppDirectory,
            splitStaticState,
            splitLink);

        return true;
    }

    private static void ValidateSplitState(
        WorkerAssignRequest request,
        string expectedPodName,
        string? workerContainerEncryptionKey,
        List<RequestValidationError> errors,
        out SplitWorkerStaticState? staticState,
        out SplitWorkerLink? link)
    {
        staticState = null;
        link = null;
        if (request.StaticState is null && request.Link is null)
        {
            return;
        }

        if (request.StaticState is not { } staticRequest)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "staticState"));
            return;
        }

        if (request.Link is not { } linkRequest)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "link"));
            return;
        }

        SplitPodIdentity? self = ValidatePodIdentity(
            staticRequest.Self, "staticState.self", WorkerPodType, errors);
        SplitWorkerLinkState? linkState = ValidateLinkState(linkRequest.State, errors);
        SplitPodIdentity? runtime = linkRequest.Runtime is null
            ? null
            : ValidatePodIdentity(linkRequest.Runtime, "link.runtime", RuntimePodType, errors);

        if (staticRequest.IsDiscoveryPod is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "staticState.isDiscoveryPod"));
        }

        if (staticRequest.IsAlwaysReady is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "staticState.isAlwaysReady"));
        }

        if (string.IsNullOrWhiteSpace(staticRequest.FunctionGroup))
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "staticState.functionGroup"));
        }

        if (staticRequest.ConfigLastModifiedTime is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "staticState.configLastModifiedTime"));
        }

        if (staticRequest.ContentLastModifiedTime is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "staticState.contentLastModifiedTime"));
        }

        bool runtimeRequired = linkState == SplitWorkerLinkState.Pending || linkState == SplitWorkerLinkState.Linked;
        if (runtimeRequired && runtime is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "link.runtime"));
        }

        ValidateLinkAcknowledgement(
            linkRequest,
            linkState,
            self,
            runtime,
            workerContainerEncryptionKey,
            errors);

        if (self is not null)
        {
            if (!string.Equals(self.PodName, expectedPodName, StringComparison.Ordinal))
            {
                errors.Add(new(WorkerApiErrorCodes.InvalidValue, "staticState.self.podName"));
            }

            if (!string.Equals(self.App.FunctionAppName, request.FunctionAppName, StringComparison.Ordinal))
            {
                errors.Add(new(WorkerApiErrorCodes.InvalidValue, "staticState.self.app.functionAppName"));
            }
        }

        if (staticRequest.IsAlwaysReady is { } splitAlwaysReady
            && request.IsAlwaysReady is { } isAlwaysReady
            && splitAlwaysReady != isAlwaysReady)
        {
            errors.Add(new(WorkerApiErrorCodes.InvalidValue, "staticState.isAlwaysReady"));
        }

        if (staticRequest.FunctionGroup is { } functionGroup
            && request.FunctionGroupName is { } functionGroupName
            && !string.Equals(functionGroup, functionGroupName, StringComparison.Ordinal))
        {
            errors.Add(new(WorkerApiErrorCodes.InvalidValue, "staticState.functionGroup"));
        }

        if (self is not null && runtime is not null
            && (!string.Equals(self.ResourceNamespace, runtime.ResourceNamespace, StringComparison.Ordinal)
                || !string.Equals(self.LegionStampName, runtime.LegionStampName, StringComparison.Ordinal)
                || self.App != runtime.App))
        {
            errors.Add(new(WorkerApiErrorCodes.InvalidValue, "link.runtime"));
        }

        if (errors.Count > 0
            || self is null
            || linkState is null
            || staticRequest.IsDiscoveryPod is not { } isDiscoveryPod
            || staticRequest.IsAlwaysReady is not { } staticAlwaysReady
            || staticRequest.FunctionGroup is not { } staticFunctionGroup
            || staticRequest.ConfigLastModifiedTime is not { } configLastModifiedTime
            || staticRequest.ContentLastModifiedTime is not { } contentLastModifiedTime)
        {
            return;
        }

        staticState = new(
            self,
            isDiscoveryPod,
            staticAlwaysReady,
            staticFunctionGroup,
            configLastModifiedTime,
            contentLastModifiedTime);
        link = new(linkState.Value, runtime);
    }

    private static void ValidateLinkAcknowledgement(
        SplitWorkerLinkRequest request,
        SplitWorkerLinkState? linkState,
        SplitPodIdentity? self,
        SplitPodIdentity? runtime,
        string? workerContainerEncryptionKey,
        List<RequestValidationError> errors)
    {
        if (linkState != SplitWorkerLinkState.Linked)
        {
            if (request.LinkAcknowledgement is not null)
            {
                errors.Add(new(WorkerApiErrorCodes.InvalidValue, "link.linkAcknowledgement"));
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(request.LinkAcknowledgement))
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "link.linkAcknowledgement"));
            return;
        }

        if (self is null || runtime is null || string.IsNullOrWhiteSpace(workerContainerEncryptionKey))
        {
            return;
        }

        Span<byte> acknowledgement = stackalloc byte[HMACSHA256.HashSizeInBytes];
        if (!Convert.TryFromBase64String(request.LinkAcknowledgement, acknowledgement, out int bytesWritten)
            || bytesWritten != acknowledgement.Length)
        {
            errors.Add(new(WorkerApiErrorCodes.InvalidValue, "link.linkAcknowledgement"));
            return;
        }

        string payload = string.Concat(
            self.PodGuid.ToString("D"),
            "\n",
            runtime.PodGuid.ToString("D"));
        byte[] expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(workerContainerEncryptionKey),
            Encoding.UTF8.GetBytes(payload));
        if (!CryptographicOperations.FixedTimeEquals(expected, acknowledgement))
        {
            errors.Add(new(WorkerApiErrorCodes.InvalidValue, "link.linkAcknowledgement"));
        }
    }

    private static SplitWorkerLinkState? ValidateLinkState(string? value, List<RequestValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "link.state"));
            return null;
        }

        if (string.Equals(value, nameof(SplitWorkerLinkState.Unlinked), StringComparison.Ordinal))
        {
            return SplitWorkerLinkState.Unlinked;
        }

        if (string.Equals(value, nameof(SplitWorkerLinkState.Pending), StringComparison.Ordinal))
        {
            return SplitWorkerLinkState.Pending;
        }

        if (string.Equals(value, nameof(SplitWorkerLinkState.Linked), StringComparison.Ordinal))
        {
            return SplitWorkerLinkState.Linked;
        }

        errors.Add(new(WorkerApiErrorCodes.InvalidValue, "link.state"));
        return null;
    }

    private static SplitPodIdentity? ValidatePodIdentity(
        SplitPodIdentityRequest? request,
        string target,
        string expectedPodType,
        List<RequestValidationError> errors)
    {
        if (request is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, target));
            return null;
        }

        bool valid = true;
        valid &= ValidateRequired(request.ResourceNamespace, $"{target}.resourceNamespace", errors);
        valid &= ValidateRequired(request.PodName, $"{target}.podName", errors);
        if (request.PodGuid is null || request.PodGuid == Guid.Empty)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, $"{target}.podGuid"));
            valid = false;
        }

        valid &= ValidateRequired(request.LegionStampName, $"{target}.legionStampName", errors);
        if (!string.Equals(request.PodType, expectedPodType, StringComparison.Ordinal))
        {
            errors.Add(new(
                string.IsNullOrWhiteSpace(request.PodType) ? WorkerApiErrorCodes.Required : WorkerApiErrorCodes.InvalidValue,
                $"{target}.podType"));
            valid = false;
        }

        SplitFunctionAppIdentity? app = ValidateFunctionAppIdentity(request.App, $"{target}.app", errors);
        if (!valid || app is null
            || request.ResourceNamespace is not { } resourceNamespace
            || request.PodName is not { } podName
            || request.PodGuid is not { } podGuid
            || request.LegionStampName is not { } legionStampName)
        {
            return null;
        }

        return new(resourceNamespace, podName, podGuid, legionStampName, expectedPodType, app);
    }

    private static SplitFunctionAppIdentity? ValidateFunctionAppIdentity(
        SplitFunctionAppIdentityRequest? request,
        string target,
        List<RequestValidationError> errors)
    {
        if (request is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, target));
            return null;
        }

        bool valid = ValidateRequired(request.FunctionAppName, $"{target}.functionAppName", errors);
        if (request.FunctionAppSiteId is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, $"{target}.functionAppSiteId"));
            valid = false;
        }

        valid &= ValidateRequired(request.AntaresStampName, $"{target}.antaresStampName", errors);
        if (!valid
            || request.FunctionAppName is not { } functionAppName
            || request.FunctionAppSiteId is not { } functionAppSiteId
            || request.AntaresStampName is not { } antaresStampName)
        {
            return null;
        }

        return new(functionAppName, functionAppSiteId, antaresStampName);
    }

    private static bool ValidateRequired(string? value, string target, List<RequestValidationError> errors)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        errors.Add(new(WorkerApiErrorCodes.Required, target));
        return false;
    }

    private static WorkerStartupMode? ValidateStartupMode(string? value, List<RequestValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "startupMode"));
            return null;
        }

        if (string.Equals(value, nameof(WorkerStartupMode.Preconfigured), StringComparison.Ordinal))
        {
            return WorkerStartupMode.Preconfigured;
        }

        if (string.Equals(value, nameof(WorkerStartupMode.SpecializationRequired), StringComparison.Ordinal))
        {
            return WorkerStartupMode.SpecializationRequired;
        }

        errors.Add(new(WorkerApiErrorCodes.InvalidValue, "startupMode"));
        return null;
    }

    private static Dictionary<string, string> ValidateAndCopyEnvironment(
        IReadOnlyDictionary<string, string?>? source, List<RequestValidationError> errors)
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal);
        if (source is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "environment"));
            return environment;
        }

        foreach ((string key, string? value) in source)
        {
            if (string.IsNullOrEmpty(key) || value is null)
            {
                // Report the invalid field once without exposing environment keys or values.
                errors.Add(new(WorkerApiErrorCodes.InvalidValue, "environment"));
                break;
            }

            environment.Add(key, value);
        }

        return environment;
    }
}
