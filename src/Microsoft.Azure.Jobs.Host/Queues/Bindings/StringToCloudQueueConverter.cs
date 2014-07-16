// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class StringToCloudQueueConverter : IConverter<string, CloudQueue>
    {
        private readonly CloudQueueClient _client;
        private readonly string _defaultQueueName;

        public StringToCloudQueueConverter(CloudQueueClient client, string defaultQueueName)
        {
            _client = client;
            _defaultQueueName = defaultQueueName;
        }

        public CloudQueue Convert(string input)
        {
            string queueName;

            // For convenience, treat an an empty string as a request for the default value.
            if (String.IsNullOrEmpty(input))
            {
                queueName = _defaultQueueName;
            }
            else
            {
                queueName = input;
            }

            return _client.GetQueueReference(queueName);
        }
    }
}
