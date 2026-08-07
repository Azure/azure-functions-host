// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Tests;

/// <summary>
/// Records process-environment writes without reading or changing the real process environment.
/// </summary>
public sealed class RecordingProcessMutator
{
    private readonly Action<string> _record;
    private readonly List<ProcessMutation> _attempts = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordingProcessMutator"/> class.
    /// </summary>
    public RecordingProcessMutator(Action<string> record = null)
    {
        _record = record;
    }

    /// <summary>
    /// Gets the exact ordered write attempts.
    /// </summary>
    public IReadOnlyList<ProcessMutation> Attempts => _attempts;

    /// <summary>
    /// Gets or sets the variable name whose write should fail after being recorded.
    /// </summary>
    public string FailureName { get; set; }

    /// <summary>
    /// Gets or sets the message used when an injected write failure is thrown.
    /// </summary>
    public string FailureMessage { get; set; }

    /// <summary>
    /// Records a write attempt.
    /// </summary>
    public void Set(string name, string value)
    {
        _attempts.Add(new ProcessMutation(name, value));
        _record?.Invoke($"write:{name}={FormatValue(value)}");

        if (string.Equals(name, FailureName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                FailureMessage ?? $"Mutation failed for '{name}'.");
        }
    }

    private static string FormatValue(string value)
    {
        return value ?? "<null>";
    }
}

/// <summary>
/// Represents one attempted process-environment write.
/// </summary>
/// <param name="Name">The variable name.</param>
/// <param name="Value">The exact value, including null or empty.</param>
public sealed record ProcessMutation(string Name, string Value);
