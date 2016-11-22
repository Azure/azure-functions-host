// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Web.MeditTypeFormatters
{
    public class JsonXmlMediaTypeFormatterTests
    {
        [Fact]
        public async Task WriteToStreamAsync_WithSinglePropertyObject_WritesExpectedXml()
        {
            var formatter = new JsonXmlMediaTypeFormatter();

            var input = new JObject
            {
                ["name"] = "Fabio"
            };

            using (var stream = new MemoryStream())
            {
                var content = new StringContent(string.Empty);

                await formatter.WriteToStreamAsync(typeof(JObject), input, stream, content, null);

                stream.Position = 0;

                using (var reader = new StreamReader(stream))
                {
                    string result = reader.ReadToEnd();
                    Assert.Equal("<?xml version=\"1.0\" encoding=\"utf-8\"?><name>Fabio</name>", result);
                }
            }
        }

        [Fact]
        public async Task WriteToStreamAsync_WithNoRootPropertyObject_WritesExpectedXml()
        {
            var formatter = new JsonXmlMediaTypeFormatter();

            var input = new JObject
            {
                ["name"] = "Fabio",
                ["lastname"] = "Cavalcante",
            };

            using (var stream = new MemoryStream())
            {
                var content = new StringContent(string.Empty);

                await formatter.WriteToStreamAsync(typeof(JObject), input, stream, content, null);

                stream.Position = 0;

                using (var reader = new StreamReader(stream))
                {
                    string result = reader.ReadToEnd();
                    Assert.Equal("<?xml version=\"1.0\" encoding=\"utf-8\"?><response><name>Fabio</name><lastname>Cavalcante</lastname></response>", result);
                }
            }
        }
    }
}
