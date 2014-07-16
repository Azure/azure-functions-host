// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Listeners
{
    internal sealed class ServiceBusListener : IListener
    {
        private readonly MessagingFactory _messagingFactory;
        private readonly string _entityPath;
        private readonly ITriggerExecutor<BrokeredMessage> _triggerExecutor;

        private MessageReceiver _receiver;
        private bool _disposed;

        public ServiceBusListener(MessagingFactory messagingFactory, string entityPath,
            ITriggerExecutor<BrokeredMessage> triggerExecutor)
        {
            _messagingFactory = messagingFactory;
            _entityPath = entityPath;
            _triggerExecutor = triggerExecutor;
        }

        public void Start()
        {
            ThrowIfDisposed();

            if (_receiver != null)
            {
                throw new InvalidOperationException("The listener has already been started.");
            }

            _receiver = _messagingFactory.CreateMessageReceiver(_entityPath);

            _receiver.OnMessage(ProcessMessage, new OnMessageOptions());
        }

        public void Stop()
        {
            ThrowIfDisposed();

            if (_receiver == null)
            {
                throw new InvalidOperationException(
                    "The listener has not yet been started or has already been stopped.");
            }

            _receiver.Close();
            _receiver = null;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_receiver != null)
                {
                    _receiver.Abort();
                    _receiver = null;
                }

                _disposed = true;
            }
        }

        private void ProcessMessage(BrokeredMessage message)
        {
            if (!_triggerExecutor.Execute(message))
            {
                message.Abandon();
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
