// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reactive.Disposables;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class, new()
    {
        private readonly Func<T> _optionsFactory;
        private Action<T, string> _listener;

        public TestOptionsMonitor(T options)
            : this(() => options ?? new T())
        {
        }

        public TestOptionsMonitor(Func<T> optionsFactory)
        {
            _optionsFactory = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
        }

        public T CurrentValue => _optionsFactory();

        public T Get(string name)
        {
            return _optionsFactory();
        }

        public IDisposable OnChange(Action<T, string> listener)
        {
            _listener = listener;
            return Disposable.Empty;
        }

        internal void InvokeChanged()
        {
            _listener?.Invoke(CurrentValue, null);
        }
    }
}
