// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal class MessageSenderAsyncCollector<T> : IAsyncCollector<T>
    {
        private readonly ServiceBusEntity _entity;
        private readonly IConverter<T, BrokeredMessage> _converter;
        private readonly Guid _functionInstanceId;

        public MessageSenderAsyncCollector(ServiceBusEntity entity, IConverter<T, BrokeredMessage> converter,
            Guid functionInstanceId)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("entity");
            }

            if (converter == null)
            {
                throw new ArgumentNullException("converter");
            }

            _entity = entity;
            _converter = converter;
            _functionInstanceId = functionInstanceId;
        }

        public Task AddAsync(T item, CancellationToken cancellationToken)
        {
            BrokeredMessage message = _converter.Convert(item);

            if (message == null)
            {
                throw new InvalidOperationException("Cannot enqueue a null brokered message instance.");
            }

            return _entity.SendAndCreateQueueIfNotExistsAsync(message, _functionInstanceId, cancellationToken);
        }
    }
}
