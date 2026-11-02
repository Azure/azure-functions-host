// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Holds the effective HTTP proxy origin published by an accepted assignment.
/// </summary>
internal sealed class WorkerHttpProxyEndpointState
{
    private readonly Lock _lock = new();
    private string? _value;

    /// <summary>
    /// Gets the effective advertised HTTP origin.
    /// </summary>
    public string? Value
    {
        get
        {
            lock (_lock)
            {
                return _value;
            }
        }
    }

    /// <summary>
    /// Replaces the effective advertised HTTP origin.
    /// </summary>
    public void Set(string? value)
    {
        lock (_lock)
        {
            _value = value;
        }
    }
}
