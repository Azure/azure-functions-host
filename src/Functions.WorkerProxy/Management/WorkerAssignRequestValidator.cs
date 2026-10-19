// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Azure.Functions.WorkerProxy.State;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Validates bound assignment requests and captures immutable identity without accessing worker state.
/// </summary>
internal static class WorkerAssignRequestValidator
{
    private const string WorkerPodType = "FunctionsSplitWorkerPod";

    /// <summary>
    /// Creates an assignment from a valid request, or collects ordered field errors without exposing environment entries.
    /// </summary>
    public static bool TryCreateAssignment(
        WorkerAssignRequest request,
        string expectedPodName,
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
        ValidateSplitStaticState(
            request,
            expectedPodName,
            errors,
            out SplitWorkerStaticState? splitStaticState);

        if (errors.Count > 0
            || startupMode is not { } selectedStartupMode
            || request.FunctionAppName is not { } functionAppName
            || request.FunctionGroupName is not { } functionGroupName
            || request.FunctionAppDirectory is not { } functionAppDirectory
            || request.IsAlwaysReady is not { } isAlwaysReady
            || splitStaticState is not { } validatedStaticState)
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
            validatedStaticState);

        return true;
    }

    private static void ValidateSplitStaticState(
        WorkerAssignRequest request,
        string expectedPodName,
        List<RequestValidationError> errors,
        out SplitWorkerStaticState? staticState)
    {
        staticState = null;
        if (request.StaticState is null)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "staticState"));
            return;
        }

        SplitWorkerStaticStateRequest staticRequest = request.StaticState;

        if (staticRequest.IsDiscoveryPod is not { } isDiscoveryPod
            || staticRequest.IsAlwaysReady is not { } staticAlwaysReady
            || string.IsNullOrWhiteSpace(staticRequest.FunctionGroup)
            || staticRequest.ConfigLastModifiedTime is not { } configLastModifiedTime
            || staticRequest.ContentLastModifiedTime is not { } contentLastModifiedTime)
        {
            errors.Add(new(WorkerApiErrorCodes.Required, "staticState"));
            return;
        }

        int initialErrorCount = errors.Count;
        SplitPodIdentity? self = ValidatePodIdentity(
            staticRequest.Self, "staticState.self", WorkerPodType, errors);

        if (self is not null)
        {
            if (!string.Equals(self.PodName, expectedPodName, StringComparison.Ordinal))
            {
                errors.Add(new(WorkerApiErrorCodes.InvalidValue, "staticState.self.podName"));
            }

            if (!string.IsNullOrWhiteSpace(request.FunctionAppName)
                && !string.Equals(self.App.FunctionAppName, request.FunctionAppName, StringComparison.Ordinal))
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
            && !string.IsNullOrWhiteSpace(functionGroupName)
            && !string.Equals(functionGroup, functionGroupName, StringComparison.Ordinal))
        {
            errors.Add(new(WorkerApiErrorCodes.InvalidValue, "staticState.functionGroup"));
        }

        if (errors.Count != initialErrorCount
            || self is null)
        {
            return;
        }

        staticState = new(
            self,
            isDiscoveryPod,
            staticAlwaysReady,
            staticRequest.FunctionGroup,
            configLastModifiedTime,
            contentLastModifiedTime);
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

        if (string.IsNullOrWhiteSpace(request.ResourceNamespace)
            || string.IsNullOrWhiteSpace(request.PodName)
            || request.PodGuid is not { } podGuid
            || podGuid == Guid.Empty
            || string.IsNullOrWhiteSpace(request.LegionStampName)
            || string.IsNullOrWhiteSpace(request.PodType)
            || request.App is not { } app
            || string.IsNullOrWhiteSpace(app.FunctionAppName)
            || app.FunctionAppSiteId is not { } functionAppSiteId
            || string.IsNullOrWhiteSpace(app.AntaresStampName))
        {
            errors.Add(new(WorkerApiErrorCodes.Required, target));
            return null;
        }

        if (!string.Equals(request.PodType, expectedPodType, StringComparison.Ordinal))
        {
            errors.Add(new(WorkerApiErrorCodes.InvalidValue, $"{target}.podType"));
            return null;
        }

        return new(
            request.ResourceNamespace,
            request.PodName,
            podGuid,
            request.LegionStampName,
            expectedPodType,
            new(app.FunctionAppName, functionAppSiteId, app.AntaresStampName));
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
