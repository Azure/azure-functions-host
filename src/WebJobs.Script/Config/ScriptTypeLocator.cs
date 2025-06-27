// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ScriptTypeLocator : ITypeLocator, IDisposable
    {
        private readonly ManualResetEventSlim _typesSetEvent;
        private readonly TimeSpan _setWaitTimeout;
        private Type[] _types;
        private bool _disposed;

        public ScriptTypeLocator()
            : this(TimeSpan.FromMinutes(2))
        { }

        internal ScriptTypeLocator(TimeSpan setWaitTimeout)
        {
            _typesSetEvent = new ManualResetEventSlim(false);
            _setWaitTimeout = setWaitTimeout;
        }

        public IReadOnlyList<Type> GetTypes()
        {
            if (!_typesSetEvent.Wait(_setWaitTimeout))
            {
                throw new TimeoutException($"Timeout waiting for types to be set in {nameof(ScriptTypeLocator)}.");
            }

            return _types;
        }

        internal void SetTypes(IEnumerable<Type> types)
        {
            ArgumentNullException.ThrowIfNull(types);

            _types = [.. types];
            _typesSetEvent.Set();
        }

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _typesSetEvent.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
