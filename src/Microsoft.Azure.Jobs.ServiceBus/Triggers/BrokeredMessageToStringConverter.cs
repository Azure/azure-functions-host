// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class BrokeredMessageToStringConverter : IAsyncConverter<BrokeredMessage, string>
    {
        public Task<string> ConvertAsync(BrokeredMessage input, CancellationToken cancellationToken)
        {
            using (Stream stream = input.GetBody<Stream>())
            {
                if (stream == null)
                {
                    return null;
                }

                using (TextReader reader = new StreamReader(stream, StrictEncodings.Utf8))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return reader.ReadToEndAsync();
                }
            }
        }
    }
}
