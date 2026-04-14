// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities;

/// <summary>
/// Change token source for <see cref="AppCapabilitiesOptions"/>.
/// Call <see cref="TriggerChange"/> to signal that capabilities have been updated
/// and options should be re-evaluated.
/// </summary>
internal sealed class AppCapabilitiesChangeTokenSource : IOptionsChangeTokenSource<AppCapabilitiesOptions>, IDisposable
{
    private CancellationTokenSource? _cts = new();

    public string Name => Options.DefaultName;

    public IChangeToken GetChangeToken()
    {
        ObjectDisposedException.ThrowIf(_cts is null, this);

        return new CancellationChangeToken(_cts.Token);
    }

    public void TriggerChange()
    {
        var newCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _cts, newCts);

        if (previousCts is null)
        {
            // Object was disposed, don't leave the new CTS behind
            Interlocked.CompareExchange(ref _cts, null, newCts);
            newCts.Dispose();
            return;
        }

        previousCts.Cancel();
        previousCts.Dispose();
    }

    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Dispose();
    }
}
