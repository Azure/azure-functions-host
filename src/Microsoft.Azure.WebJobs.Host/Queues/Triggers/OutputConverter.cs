// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Triggers
{
    internal class OutputConverter<TInput> : IObjectToTypeConverter<IStorageQueueMessage>
        where TInput : class
    {
        private readonly IConverter<TInput, IStorageQueueMessage> _innerConverter;

        public OutputConverter(IConverter<TInput, IStorageQueueMessage> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool TryConvert(object input, out IStorageQueueMessage output)
        {
            TInput typedInput = input as TInput;

            if (typedInput == null)
            {
                output = null;
                return false;
            }

            output = _innerConverter.Convert(typedInput);
            return true;
        }
    }
}
