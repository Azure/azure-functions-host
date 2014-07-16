// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal sealed class CompositeDisposable : IDisposable
    {
        private readonly IEnumerable<IDisposable> _disposables;

        private bool _disposed;

        public CompositeDisposable(IEnumerable<IDisposable> disposables)
        {
            _disposables = disposables;
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
