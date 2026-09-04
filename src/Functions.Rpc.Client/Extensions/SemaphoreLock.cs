// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Releases an acquired semaphore when disposed.
/// </summary>
/// <remarks>This value must be disposed exactly once.</remarks>
internal readonly struct SemaphoreLock(SemaphoreSlim semaphore) : IDisposable
{
    public void Dispose()
    {
        semaphore?.Release();
    }
}
