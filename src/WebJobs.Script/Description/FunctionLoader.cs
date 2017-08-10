// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal sealed class FunctionLoader<T> : IDisposable
    {
        private readonly ReaderWriterLockSlim _functionValueLoaderLock = new ReaderWriterLockSlim();
        private readonly Func<CancellationToken, Task<T>> _valueFactory;
        private AsyncLazy<T> _functionValueLoader;
        private bool _disposed = false;

        public FunctionLoader(Func<CancellationToken, Task<T>> valueFactory)
        {
            _valueFactory = valueFactory;
            _functionValueLoader = new AsyncLazy<T>(valueFactory, new CancellationTokenSource());
        }

        public void Reset()
        {
            _functionValueLoaderLock.EnterWriteLock();
            try
            {
                _functionValueLoader?.Dispose();

                _functionValueLoader = new AsyncLazy<T>(_valueFactory, new CancellationTokenSource());
            }
            finally
            {
                _functionValueLoaderLock.ExitWriteLock();
            }
        }

        public async Task<T> GetFunctionTargetAsync(int attemptCount = 0)
        {
            AsyncLazy<T> currentValueLoader;
            _functionValueLoaderLock.EnterReadLock();
            try
            {
                currentValueLoader = _functionValueLoader;
            }
            finally
            {
                _functionValueLoaderLock.ExitReadLock();
            }

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
    }
}
