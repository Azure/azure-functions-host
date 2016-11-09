// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class PlaintextMediaTypeFormatter : MediaTypeFormatter
    {
        private static readonly Type StringType = typeof(string);

        public PlaintextMediaTypeFormatter()
        {
            SupportedMediaTypes.Add(new MediaTypeHeaderValue("text/plain"));
            SupportedEncodings.Add(Encoding.UTF8);
            SupportedEncodings.Add(Encoding.Unicode);
        }
        public override bool CanReadType(Type type)
        {
            return type == StringType;
        }

        public override bool CanWriteType(Type type)
        {
            return type == StringType;
        }

        public override async Task<object> ReadFromStreamAsync(Type type, Stream readStream, HttpContent content, IFormatterLogger formatterLogger)
        {
            Encoding selectedEncoding = SelectCharacterEncoding(content.Headers);
            using (var reader = new StreamReader(readStream, selectedEncoding))
            {
                return await reader.ReadToEndAsync();
            }
        }

        public override Task WriteToStreamAsync(Type type, object value, Stream writeStream, HttpContent content, TransportContext transportContext, CancellationToken cancellationToken)
        {
            if (value == null)
            {
                return Task.CompletedTask;
            }

            Encoding selectedEncoding = SelectCharacterEncoding(content.Headers);
            using (var writer = new StreamWriter(writeStream, selectedEncoding, bufferSize: 1024, leaveOpen: true))
            {
                return writer.WriteAsync((string)value);
            }
        }
    }
}
