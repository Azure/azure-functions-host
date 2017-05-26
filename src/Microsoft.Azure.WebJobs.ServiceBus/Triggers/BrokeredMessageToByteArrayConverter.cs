// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.ServiceBus;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class BrokeredMessageToByteArrayConverter : IAsyncConverter<Message, byte[]>
    {
        public Task<byte[]> ConvertAsync(Message input, CancellationToken cancellationToken)
        {
            return Task.FromResult(input.Body);
        }
    }
}