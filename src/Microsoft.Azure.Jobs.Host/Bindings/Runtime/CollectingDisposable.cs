// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Bindings.Runtime
{
    internal sealed class CollectingDisposable : IDisposable
    {
        private readonly IList<IDisposable> _disposables = new List<IDisposable>();

        private bool _disposed;

        public void Add(IDisposable disposable)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }

            _disposables.Add(disposable);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (IDisposable disposable in _disposables)
                {
                    disposable.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
