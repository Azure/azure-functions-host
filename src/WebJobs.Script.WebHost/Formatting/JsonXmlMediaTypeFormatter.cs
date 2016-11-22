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
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class JsonXmlMediaTypeFormatter : MediaTypeFormatter
    {
        private static readonly Type JObjectType = typeof(JObject);

        public JsonXmlMediaTypeFormatter()
        {
            SupportedMediaTypes.Add(new MediaTypeHeaderValue("text/xml"));
            SupportedEncodings.Add(Encoding.UTF8);
            SupportedEncodings.Add(Encoding.Unicode);
        }
        public override bool CanReadType(Type type)
        {
            return false;
        }

        public override bool CanWriteType(Type type)
        {
            return type == JObjectType;
        }

        public override Task<object> ReadFromStreamAsync(Type type, Stream readStream, HttpContent content, IFormatterLogger formatterLogger)
        {
            throw new NotSupportedException();
        }

        public override async Task WriteToStreamAsync(Type type, object value, Stream writeStream, HttpContent content, TransportContext transportContext, CancellationToken cancellationToken)
        {
            JObject payload = value as JObject;

            if (payload == null)
            {
                return;
            }

            // If we don't have a single root property, wrap the object
            string payloadJson = payload.Count == 1
                ? payload.ToString()
                : JsonConvert.SerializeObject(new { response = payload });

            XmlDocument responseDocument = JsonConvert.DeserializeXmlNode(payloadJson);

            Encoding selectedEncoding = SelectCharacterEncoding(content.Headers);
            using (var writer = XmlWriter.Create(writeStream, new XmlWriterSettings { Encoding = selectedEncoding, Async = true }))
            {
                responseDocument.WriteTo(writer);
                await writer.FlushAsync();
            }
        }
    }
}
