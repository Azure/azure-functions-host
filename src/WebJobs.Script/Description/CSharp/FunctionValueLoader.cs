// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Lazyly loads the function value, guaranteeing single execution, and exposing 
    /// a <see cref="Task{MethodInfo}"/> value that completes when the provided
    /// factory completes the function creation.
    /// </summary>
    internal sealed class FunctionValueLoader : Lazy<Task<MethodInfo>>, IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private bool _disposed = false;

        private FunctionValueLoader(Func<CancellationToken, MethodInfo> valueFactory, CancellationTokenSource cts)
            : base(() => Task.Factory.StartNew(() => valueFactory(cts.Token)), LazyThreadSafetyMode.ExecutionAndPublication)
        {
            _cts = cts;
            _cts.Token.Register(CancellationRequested, false);
        }

        public static FunctionValueLoader Create(Func<CancellationToken, MethodInfo> valueFactory)
        {
            return new FunctionValueLoader(valueFactory, new CancellationTokenSource());
        }

        private void CancellationRequested()
        {
            // We'll give the factory some time to process cancellation, 
            // then dispose of our token
            Task.Delay(100000)
                .ContinueWith(t =>
                {
                    try
                    {
                        _cts.Dispose();
                    }
                    catch
                    {
                    }
                });
        }

        public TaskAwaiter<MethodInfo> GetAwaiter()
        {
            return Value.GetAwaiter();
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cts.Cancel();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
