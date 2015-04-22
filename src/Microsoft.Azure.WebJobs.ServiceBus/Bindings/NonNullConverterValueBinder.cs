// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    // Same as ConverterValueBinder, but doesn't enqueue null values.
    internal class NonNullConverterValueBinder<TInput> : IOrderedValueBinder
    {
        private readonly ServiceBusEntity _entity;
        private readonly IConverter<TInput, BrokeredMessage> _converter;
        private readonly Guid _functionInstanceId;

        public NonNullConverterValueBinder(ServiceBusEntity entity, IConverter<TInput, BrokeredMessage> converter,
            Guid functionInstanceId)
        {
            _entity = entity;
            _converter = converter;
            _functionInstanceId = functionInstanceId;
        }

        public int StepOrder
        {
            get { return BindStepOrders.Enqueue; }
        }

        public Type Type
        {
            get { return typeof(TInput); }
        }

        public object GetValue()
        {
            return null;
        }

        public string ToInvokeString()
        {
            return _entity.MessageSender.Path;
        }

        public Task SetValueAsync(object value, CancellationToken cancellationToken)
        {
            if (value == null)
            {
                return Task.FromResult(0);
            }

            Debug.Assert(value is TInput);
            BrokeredMessage message = _converter.Convert((TInput)value);
            Debug.Assert(message != null);
            return _entity.SendAndCreateQueueIfNotExistsAsync(message, _functionInstanceId, cancellationToken);
        }
    }
}
