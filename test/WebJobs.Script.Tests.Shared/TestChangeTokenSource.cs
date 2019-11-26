// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestChangeTokenSource<T> : IOptionsChangeTokenSource<T>
    {
        private TestChangeToken _changeToken;

        public TestChangeTokenSource()
        {
            _changeToken = new TestChangeToken();
        }

        public string Name { get; set; }

        public void SignalChange()
        {
            var token = Interlocked.Exchange(ref _changeToken, new TestChangeToken());
            token.SignalChange();
        }

        public IChangeToken GetChangeToken()
        {
            return _changeToken;
        }

        private class TestChangeToken : IChangeToken
        {
            private CancellationTokenSource _cts = new CancellationTokenSource();

            public bool ActiveChangeCallbacks => true;

            public bool HasChanged => _cts.IsCancellationRequested;

            public IDisposable RegisterChangeCallback(Action<object> callback, object state) => _cts.Token.Register(callback, state);

            public void SignalChange()
            {
                _cts.Cancel();
                _cts.Dispose();
            }
        }
    }
}