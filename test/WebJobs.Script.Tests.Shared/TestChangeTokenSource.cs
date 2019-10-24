// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestChangeTokenSource<T> : IOptionsChangeTokenSource<T>
    {
        private IChangeToken _changeToken;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public TestChangeTokenSource()
        {
            _changeToken = new CancellationChangeToken(_cts.Token);
        }

        public string Name { get; set; }

        public void SignalChange()
        {
            Interlocked.Exchange(ref _changeToken, NullChangeToken.Singleton);
            _cts.Cancel();
            _cts.Dispose();
        }

        public IChangeToken GetChangeToken()
        {
            return _changeToken;
        }
    }
}