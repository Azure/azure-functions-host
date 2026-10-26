// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Captures immutable assignment identity, including the caller-selected startup mode.
/// </summary>
/// <remarks>
/// Environment and app directory participate in retry equality only; this model does not apply them
/// to the worker. Keep environment values out of published pod snapshots and diagnostic output.
/// </remarks>
internal sealed class WorkerAssignment
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerAssignment"/> class.
    /// </summary>
    /// <remarks>
    /// Copies environment entries using ordinal key equality. A directory is required in both modes,
    /// but only SpecializationRequired requires it to be nonblank. No filesystem check is performed.
    /// </remarks>
    public WorkerAssignment(
        WorkerStartupMode startupMode,
        string functionAppName,
        string functionGroupName,
        bool isAlwaysReady,
        IReadOnlyDictionary<string, string> environment,
        string functionAppDirectory,
        string? httpProxyEndpoint = null)
    {
        if (startupMode is not (WorkerStartupMode.Preconfigured or WorkerStartupMode.SpecializationRequired))
        {
            throw new ArgumentOutOfRangeException(nameof(startupMode));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(functionAppName);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionGroupName);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(functionAppDirectory);
        if (startupMode is WorkerStartupMode.SpecializationRequired)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(functionAppDirectory);
        }

        if (httpProxyEndpoint is not null
            && WorkerProxyOptionsValidator.ResolveHttpProxyOrigin(httpProxyEndpoint, Uri.UriSchemeHttp) is null)
        {
            throw new ArgumentException("The HTTP proxy endpoint must be a scheme-less host:port with an explicit nonzero port.", nameof(httpProxyEndpoint));
        }

        foreach ((string key, string value) in environment)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            ArgumentNullException.ThrowIfNull(value);
        }

        StartupMode = startupMode;
        FunctionAppName = functionAppName;
        FunctionGroupName = functionGroupName;
        IsAlwaysReady = isAlwaysReady;
        // Read-only input can still wrap a mutable dictionary. Freeze it so later caller edits cannot change retry identity.
        Environment = environment.ToFrozenDictionary(StringComparer.Ordinal);
        FunctionAppDirectory = functionAppDirectory;
        HttpProxyEndpoint = httpProxyEndpoint;
    }

    /// <summary>
    /// Gets the startup mode fixed by the accepted assignment.
    /// </summary>
    public WorkerStartupMode StartupMode { get; }

    public string FunctionAppName { get; }

    public string FunctionGroupName { get; }

    public bool IsAlwaysReady { get; }

    public FrozenDictionary<string, string> Environment { get; }

    public string FunctionAppDirectory { get; }

    /// <summary>
    /// Gets the optional scheme-less mapped host:port frozen by the accepted assignment.
    /// The original string participates in ordinal retry equality, like other assignment strings.
    /// </summary>
    public string? HttpProxyEndpoint { get; }

    /// <summary>
    /// Compares all assignment fields without depending on dictionary order or the caller's comparer.
    /// </summary>
    public bool IsEquivalentTo(WorkerAssignment other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (StartupMode != other.StartupMode
            || !string.Equals(FunctionAppName, other.FunctionAppName, StringComparison.Ordinal)
            || !string.Equals(FunctionGroupName, other.FunctionGroupName, StringComparison.Ordinal)
            || IsAlwaysReady != other.IsAlwaysReady
            || !string.Equals(FunctionAppDirectory, other.FunctionAppDirectory, StringComparison.Ordinal)
            || !string.Equals(HttpProxyEndpoint, other.HttpProxyEndpoint, StringComparison.Ordinal)
            || Environment.Count != other.Environment.Count)
        {
            return false;
        }

        foreach ((string key, string value) in Environment)
        {
            if (!other.Environment.TryGetValue(key, out string? otherValue)
                || !string.Equals(value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
