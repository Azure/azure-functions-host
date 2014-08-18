// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Listeners
{
    internal sealed class ServiceBusListener : IListener
    {
        private readonly MessagingFactory _messagingFactory;
        private readonly string _entityPath;
        private readonly ITriggerExecutor<BrokeredMessage> _triggerExecutor;
        private readonly CancellationTokenSource _cancellationTokenSource;

        private MessageReceiver _receiver;
        private bool _disposed;

        public ServiceBusListener(MessagingFactory messagingFactory, string entityPath,
            ITriggerExecutor<BrokeredMessage> triggerExecutor)
        {
            _messagingFactory = messagingFactory;
            _entityPath = entityPath;
            _triggerExecutor = triggerExecutor;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (_receiver != null)
            {
                throw new InvalidOperationException("The listener has already been started.");
            }

            return StartAsyncCore(cancellationToken);
        }

        private async Task StartAsyncCore(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _receiver = await _messagingFactory.CreateMessageReceiverAsync(_entityPath);

            _receiver.OnMessageAsync(ProcessMessageAsync, new OnMessageOptions());
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (_receiver == null)
            {
                throw new InvalidOperationException(
                    "The listener has not yet been started or has already been stopped.");
            }

            // Signal ProcessMessage to shut down gracefully
            _cancellationTokenSource.Cancel();

            return StopAsyncCore(cancellationToken);
        }

        private async Task StopAsyncCore(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _receiver.CloseAsync();
            _receiver = null;
        }

        public void Cancel()
        {
            ThrowIfDisposed();
            _cancellationTokenSource.Cancel();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Running callers might still be using the cancellation token.
                // Mark it canceled but don't dispose of the source while the callers are running.
                // Otherwise, callers would receive ObjectDisposedException when calling token.Register.
                // For now, rely on finalization to clean up _cancellationTokenSource's wait handle (if allocated).
                _cancellationTokenSource.Cancel();

                if (_receiver != null)
                {
                    _receiver.Abort();
                    _receiver = null;
                }

                _disposed = true;
            }
        }

        private Task ProcessMessageAsync(BrokeredMessage message)
        {
            return ProcessMessageAsync(message, _cancellationTokenSource.Token);
        }

        private async Task ProcessMessageAsync(BrokeredMessage message, CancellationToken cancellationToken)
        {
            if (!await _triggerExecutor.ExecuteAsync(message, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await message.AbandonAsync();
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
