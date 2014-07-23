// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class OutputConverter<TInput> : IAsyncObjectToTypeConverter<ServiceBusEntity>
        where TInput : class
    {
        private readonly IAsyncConverter<TInput, ServiceBusEntity> _innerConverter;

        public OutputConverter(IAsyncConverter<TInput, ServiceBusEntity> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public async Task<ConversionResult<ServiceBusEntity>> TryConvertAsync(object input,
            CancellationToken cancellationToken)
        {
            TInput typedInput = input as TInput;

            if (typedInput == null)
            {
                return new ConversionResult<ServiceBusEntity>
                {
                    Succeeded = false,
                    Result = null
                };
            }

            ServiceBusEntity entity = await _innerConverter.ConvertAsync(typedInput, cancellationToken);

            return new ConversionResult<ServiceBusEntity>
            {
                Succeeded = true,
                Result = entity
            };
        }
    }
}
