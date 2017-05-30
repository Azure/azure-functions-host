// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class RpcRequestStreamWatcher : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly CancellationToken _cancellationToken;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _disposed = false;

        public RpcRequestStreamWatcher()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;
            InitializeWatcher();
        }

        ~RpcRequestStreamWatcher()
        {
            Dispose(false);
        }

        public event EventHandler<RpcMessageReceivedEventArgs> RpcMessageReceived;

        private void InitializeWatcher()
        {
            lock (_syncRoot)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                RpcMessageReceived += OnRpcMessageReceived;
            }
        }

        protected void OnRpcMessageReceived(object sender, RpcMessageReceivedEventArgs e)
        {
            RpcMessageReceived?.Invoke(this, e);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                }
                _cancellationTokenSource = null;
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
