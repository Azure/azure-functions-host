// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Tests.Specialization;

internal interface IProcessEnvironmentMutatorContract
{
    void Set(string name, string value);
}

internal sealed record ProcessMutation(string Name, string Value);

internal sealed class RecordingProcessMutator : IProcessEnvironmentMutatorContract
{
    private readonly Action<string> _record;
    private readonly List<ProcessMutation> _attempts = [];

    public RecordingProcessMutator(Action<string> record)
    {
        _record = record;
    }

    public IReadOnlyList<ProcessMutation> Attempts => _attempts;

    public string FailureName { get; set; }

    public void Set(string name, string value)
    {
        _attempts.Add(new ProcessMutation(name, value));
        _record?.Invoke($"write:{name}={FormatValue(value)}");

        if (string.Equals(name, FailureName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Mutation failed for '{name}'.");
        }
    }

    private static string FormatValue(string value)
    {
        return value ?? "<null>";
    }
}
