// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public sealed class HostBuiltChangeTokenSource<TOptions> : IOptionsChangeTokenSource<TOptions>, IDisposable
    {
        private CancellationTokenSource _cts = new();

        public string Name => Options.DefaultName;

        public IChangeToken GetChangeToken() => new CancellationChangeToken(_cts.Token);

        public void TriggerChange()
        {
            var previousCts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
            previousCts.Cancel();
            previousCts.Dispose();
        }

        public void Dispose() => _cts.Dispose();
    }
}