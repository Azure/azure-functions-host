// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal sealed class FunctionLoader<T> : IDisposable
    {
        private readonly Func<CancellationToken, Task<T>> _valueFactory;
        private FunctionValueLoader<T> _functionValueLoader;
        private bool _disposed = false;

        public FunctionLoader(Func<CancellationToken, Task<T>> valueFactory)
        {
            _valueFactory = valueFactory;
            _functionValueLoader = new FunctionValueLoader<T>(valueFactory, new CancellationTokenSource());
        }

        public void Reset()
        {
            var old = Interlocked.Exchange(ref _functionValueLoader, new FunctionValueLoader<T>(_valueFactory, new CancellationTokenSource()));
            old?.Dispose();
        }

        public async Task<T> GetFunctionTargetAsync(int attemptCount = 0)
        {
            FunctionValueLoader<T> currentValueLoader = _functionValueLoader;

            try
            {
                return await currentValueLoader;
            }
            catch (OperationCanceledException)
            {
                // If the current task we were awaiting on was cancelled due to a
                // cache refresh, retry, which will use the new loader
                if (attemptCount > 2)
                {
                    throw;
                }
            }

            return await GetFunctionTargetAsync(++attemptCount);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _functionValueLoader.Dispose();
                _disposed = true;
            }
        }

        public void Dispose() => Dispose(true);

        /// <summary>
        /// Lazily loads the function value, guaranteeing single execution, and exposing
        /// a <see cref="Task{T}"/> value that completes when the provided
        /// factory completes the function creation.
        /// </summary>
        private sealed class FunctionValueLoader<U> : Lazy<Task<U>>, IDisposable
        {
            private readonly CancellationTokenSource _cts;
            private bool _disposed = false;

            internal FunctionValueLoader(Func<CancellationToken, Task<U>> valueFactory, CancellationTokenSource cts)
                : base(() => valueFactory(cts.Token), LazyThreadSafetyMode.ExecutionAndPublication)
            {
                _cts = cts;
                _cts.Token.Register(CancellationRequested, false);
            }

            private void CancellationRequested()
            {
                // We'll give the factory some time to process cancellation,
                // then dispose of our token
                Task.Delay(30000)
                    .ContinueWith(t =>
                    {
                        try
                        {
                            _cts.Dispose();
                        }
                        catch
                        {
                        }
                    }, TaskContinuationOptions.ExecuteSynchronously);
            }

            public TaskAwaiter<U> GetAwaiter()
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

            public void Dispose() => Dispose(true);
        }
    }
}
