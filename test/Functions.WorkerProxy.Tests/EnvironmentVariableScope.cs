// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _previousValue;

    public EnvironmentVariableScope(string name, string? value)
    {
        _name = name;
        _previousValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, _previousValue);
    }
}

[CollectionDefinition(nameof(EnvironmentVariableCollection), DisableParallelization = true)]
public sealed class EnvironmentVariableCollection
{
}
