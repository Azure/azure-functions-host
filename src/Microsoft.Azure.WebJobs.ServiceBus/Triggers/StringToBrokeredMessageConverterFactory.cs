// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class StringToBrokeredMessageConverterFactory
    {
        public static IConverter<string, BrokeredMessage> Create(Type parameterType)
        {
            if (parameterType == typeof(BrokeredMessage) || parameterType == typeof(string))
            {
                return new StringToTextBrokeredMessageConverter();
            }
            else if (parameterType == typeof(byte[]))
            {
                return new StringToBinaryBrokeredMessageConverter();
            }
            else
            {
                return new StringToJsonBrokeredMessageConverter();
            }
        }
    }
}
