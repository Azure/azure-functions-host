// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Azure.Functions.Rpc.Client.Tests;

internal sealed class TrackingDisposable : IDisposable
{
    private readonly Exception _disposeException;
    private int _disposeCount;

    internal TrackingDisposable(Exception disposeException = null)
    {
        _disposeException = disposeException;
    }

    internal int DisposeCount => Interlocked.CompareExchange(ref _disposeCount, 0, 0);

    public void Dispose()
    {
        Interlocked.Increment(ref _disposeCount);
        if (_disposeException is not null)
        {
            throw _disposeException;
        }
    }
}
