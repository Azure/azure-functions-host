// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace WebJobs.Script.WebHost
{
    public sealed class SuspendedSynchronizationContextScope : IDisposable
    {
        private readonly SynchronizationContext _oldContext;
        private bool _disposed = false;

        public SuspendedSynchronizationContextScope()
        {
            _oldContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    SynchronizationContext.SetSynchronizationContext(_oldContext);
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
