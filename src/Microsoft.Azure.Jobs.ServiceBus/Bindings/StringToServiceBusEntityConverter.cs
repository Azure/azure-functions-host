// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class StringToServiceBusEntityConverter : IConverter<string, ServiceBusEntity>
    {
        private readonly ServiceBusAccount _account;
        private readonly string _defaultQueueOrTopicName;

        public StringToServiceBusEntityConverter(ServiceBusAccount account, string defaultQueueOrTopicName)
        {
            _account = account;
            _defaultQueueOrTopicName = defaultQueueOrTopicName;
        }

        public ServiceBusEntity Convert(string input)
        {
            string queueOrTopicName;

            // For convenience, treat an an empty string as a request for the default value.
            if (String.IsNullOrEmpty(input))
            {
                queueOrTopicName = _defaultQueueOrTopicName;
            }
            else
            {
                queueOrTopicName = input;
            }

            return new ServiceBusEntity
            {
                Account = _account,
                MessageSender = _account.MessagingFactory.CreateMessageSender(queueOrTopicName)
            };
        }
    }
}
