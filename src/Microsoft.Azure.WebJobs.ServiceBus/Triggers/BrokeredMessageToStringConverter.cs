// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class BrokeredMessageToStringConverter : IAsyncConverter<BrokeredMessage, string>
    {
        public Task<string> ConvertAsync(BrokeredMessage input, CancellationToken cancellationToken)
        {
            if (input.ContentType == ContentTypes.TextPlain)
            {
                using (Stream stream = input.GetBody<Stream>())
                {
                    if (stream == null)
                    {
                        return Task.FromResult<string>(null);
                    }

                    using (TextReader reader = new StreamReader(stream, StrictEncodings.Utf8))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return reader.ReadToEndAsync();
                    }
                }
            }
            else
            {
                string contents = input.GetBody<string>();
                return Task.FromResult(contents);
            }
        }
    }
}
