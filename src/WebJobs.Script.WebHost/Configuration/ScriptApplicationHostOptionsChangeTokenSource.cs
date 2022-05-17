// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class ScriptApplicationHostOptionsChangeTokenSource : IOptionsChangeTokenSource<ScriptApplicationHostOptions>, IDisposable
    {
        private readonly IDisposable _standbyOptionsOnChangeSubscription;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private IChangeToken _changeToken;

        public ScriptApplicationHostOptionsChangeTokenSource(IOptionsMonitor<StandbyOptions> standbyOptions)
        {
            _changeToken = new CancellationChangeToken(_cancellationTokenSource.Token);

            _standbyOptionsOnChangeSubscription = standbyOptions.OnChange(o =>
            {
                if (_cancellationTokenSource == null)
                {
                    return;
                }

                // This should only ever happen once, on specialization, so null everything out
                // when this fires.
                var tokenSource = Interlocked.Exchange(ref _cancellationTokenSource, null);

                if (tokenSource != null &&
                    !tokenSource.IsCancellationRequested)
                {
                    var changeToken = Interlocked.Exchange(ref _changeToken, NullChangeToken.Singleton);

                    tokenSource.Cancel();

                    // Dispose of the token source so our change
                    // token reflects that state
                    tokenSource.Dispose();
                }
            });
        }

        public string Name { get; }

        public void Dispose() => _standbyOptionsOnChangeSubscription?.Dispose();

        public IChangeToken GetChangeToken() => _changeToken;
    }
}
