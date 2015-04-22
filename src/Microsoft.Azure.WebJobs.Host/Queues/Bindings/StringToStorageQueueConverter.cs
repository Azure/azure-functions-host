// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class StringToStorageQueueConverter : IConverter<string, IStorageQueue>
    {
        private readonly IStorageQueueClient _client;
        private readonly IBindableQueuePath _defaultPath;

        public StringToStorageQueueConverter(IStorageQueueClient client, IBindableQueuePath defaultPath)
        {
            _client = client;
            _defaultPath = defaultPath;
        }

        public IStorageQueue Convert(string input)
        {
            string queueName;

            // For convenience, treat an an empty string as a request for the default value.
            if (String.IsNullOrEmpty(input) && _defaultPath.IsBound)
            {
                queueName = _defaultPath.Bind(null);
            }
            else
            {
                queueName = BindableQueuePath.NormalizeAndValidate(input);
            }

            return _client.GetQueueReference(queueName);
        }
    }
}
