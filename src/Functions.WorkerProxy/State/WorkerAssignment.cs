// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Captures immutable assignment identity for an already-specialized worker.
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
    /// Copies environment entries using ordinal key equality.
    /// </remarks>
    public WorkerAssignment(
        string functionAppName,
        string functionGroupName,
        bool isAlwaysReady,
        IReadOnlyDictionary<string, string> environment,
        string functionAppDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionAppName);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionGroupName);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionAppDirectory);

        foreach ((string key, string value) in environment)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            ArgumentNullException.ThrowIfNull(value);
        }

        FunctionAppName = functionAppName;
        FunctionGroupName = functionGroupName;
        IsAlwaysReady = isAlwaysReady;
        // Read-only input can still wrap a mutable dictionary. Freeze it so later caller edits cannot change retry identity.
        Environment = environment.ToFrozenDictionary(StringComparer.Ordinal);
        FunctionAppDirectory = functionAppDirectory;
    }

    public string FunctionAppName { get; }

    public string FunctionGroupName { get; }

    public bool IsAlwaysReady { get; }

    public FrozenDictionary<string, string> Environment { get; }

    public string FunctionAppDirectory { get; }

    /// <summary>
    /// Compares all assignment fields without depending on dictionary order or the caller's comparer.
    /// </summary>
    public bool IsEquivalentTo(WorkerAssignment other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!string.Equals(FunctionAppName, other.FunctionAppName, StringComparison.Ordinal)
            || !string.Equals(FunctionGroupName, other.FunctionGroupName, StringComparison.Ordinal)
            || IsAlwaysReady != other.IsAlwaysReady
            || !string.Equals(FunctionAppDirectory, other.FunctionAppDirectory, StringComparison.Ordinal)
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
