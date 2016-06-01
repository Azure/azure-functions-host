// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            if (input.ContentType == ContentTypes.TextPlain ||
                input.ContentType == ContentTypes.ApplicationOctetStream ||
                input.ContentType == ContentTypes.ApplicationJson)
            {
                Stream stream = input.GetBody<Stream>();
                if (stream == null)
                {
                    return Task.FromResult<string>(null);
                }

                try
                {
                    using (TextReader reader = new StreamReader(stream, StrictEncodings.Utf8))
                    {
                        stream = null;
                        cancellationToken.ThrowIfCancellationRequested();
                        return reader.ReadToEndAsync();
                    }
                }
                finally
                {
                    if (stream != null)
                    {
                        stream.Dispose();
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
