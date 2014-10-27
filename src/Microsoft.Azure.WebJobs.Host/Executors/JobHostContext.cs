// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal sealed class JobHostContext : IDisposable
    {
        private readonly IFunctionIndexLookup _functionLookup;
        private readonly IFunctionExecutor _executor;
        private readonly IListener _listener;
        private readonly TextWriter _log;

        private bool _disposed;

        public JobHostContext(IFunctionIndexLookup functionLookup,
            IFunctionExecutor executor,
            IListener listener,
            TextWriter log)
        {
            _functionLookup = functionLookup;
            _executor = executor;
            _listener = listener;
            _log = log;
        }

        public TextWriter Log
        {
            get
            {
                ThrowIfDisposed();
                return _log;
            }
        }

        public IFunctionIndexLookup FunctionLookup
        {
            get
            {
                ThrowIfDisposed();
                return _functionLookup;
            }
        }

        public IFunctionExecutor Executor
        {
            get
            {
                ThrowIfDisposed();
                return _executor;
            }
        }

        public IListener Listener
        {
            get
            {
                ThrowIfDisposed();
                return _listener;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                _listener.Dispose();

                _disposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }
    }
}
