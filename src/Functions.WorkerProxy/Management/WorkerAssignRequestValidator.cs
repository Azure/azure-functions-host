// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Azure.Functions.WorkerProxy.State;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Validates bound assignment requests and captures immutable identity without accessing worker state.
/// </summary>
internal static class WorkerAssignRequestValidator
{
    /// <summary>
    /// Creates an assignment from a valid request, or collects ordered field errors without exposing environment entries.
    /// </summary>
    public static bool TryCreateAssignment(
        WorkerAssignRequest request,
        [NotNullWhen(true)] out WorkerAssignment? assignment,
        out IReadOnlyList<RequestValidationError> validationErrors)
    {
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
        Uri? httpProxyEndpoint = ValidateHttpProxyEndpoint(request.HttpProxyEndpoint, errors);
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
            httpProxyEndpoint);

        return true;
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

    private static Uri? ValidateHttpProxyEndpoint(string? value, List<RequestValidationError> errors)
    {
        if (value is null)
        {
            return null;
        }

        int separator = value.LastIndexOf(':');
        if (separator <= 0
            || separator == value.Length - 1
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !TryParseIpv4Address(value[..separator], out IPAddress? address)
            || !int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > IPEndPoint.MaxPort)
        {
            errors.Add(new(WorkerApiErrorCodes.InvalidValue, "httpProxyEndpoint"));
            return null;
        }

        return new UriBuilder(Uri.UriSchemeHttp, address.ToString(), port).Uri;
    }

    private static bool TryParseIpv4Address(string value, [NotNullWhen(true)] out IPAddress? address)
    {
        address = null;
        string[] octets = value.Split('.');
        if (octets.Length != 4)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[4];
        for (int index = 0; index < octets.Length; index++)
        {
            if (!byte.TryParse(octets[index], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[index]))
            {
                return false;
            }
        }

        address = new IPAddress(bytes);
        return true;
    }
}
