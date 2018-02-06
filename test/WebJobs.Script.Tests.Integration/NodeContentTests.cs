// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class NodeContentTests : IClassFixture<NodeContentTests.TestFixture>
    {
        public NodeContentTests(TestFixture fixture)
        {
            Fixture = fixture;
        }

        public TestFixture Fixture { get; set; }

        [Fact]
        public async Task StringTextPlainResponse()
        {
            var str = "asdf";
            var content = await Response(str, "text/plain; charset=utf-8");
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task StringTextPlainReturn()
        {
            var str = "asdf";
            var content = await Return(str, "text/plain; charset=utf-8");
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task StringTextPlainRaw()
        {
            var str = "asdf";
            var content = await Raw(str, "text/plain; charset=utf-8");
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ByteArrayTextPlainResponse()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var content = await Response(bytes, "text/plain; charset=utf-8");
            Assert.Equal(str, content);
        }

        // consider supporting text/plain formatting for byte[] type
        [Fact]
        public async Task ByteArrayTextPlainReturn()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            var content = await Return(bytes, "text/plain; charset=utf-8", "application/json; charset=utf-8");
            Assert.Equal("\"" + base64 + "\"", content);
        }

        [Fact]
        public async Task ByteArrayTextPlainRaw()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var content = await Raw(bytes, "text/plain; charset=utf-8");
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ObjectTextPlainResponse()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            var content = await Response(obj, "text/plain; charset=utf-8");
            Assert.Equal(str, Regex.Replace(content, @"\s+", string.Empty));
        }

        // consider supporting text/plain conversion for expandoobject type
        [Fact]
        public async Task ObjectTextPlainReturn()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            var content = await Return(obj, "text/plain; charset=utf-8", "application/json; charset=utf-8");
            Assert.Equal(str, Regex.Replace(content, @"\s+", string.Empty));
        }

        [Fact]
        public async Task ObjectTextPlainRaw()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            var content = await Raw(obj, "text/plain; charset=utf-8");
            Assert.Equal(str, Regex.Replace(content, @"\s+", string.Empty));
        }

        [Fact]
        public async Task StringJsonResponse()
        {
            var content = await Response("asdf", "application/json; charset=utf-8");
            Assert.Equal("\"asdf\"", content);
        }

        [Fact]
        public async Task StringJsonReturn()
        {
            var content = await Return("asdf", "application/json; charset=utf-8");
            Assert.Equal("\"asdf\"", content);
        }

        [Fact]
        public async Task StringJsonRaw()
        {
            var content = await Raw("asdf", "application/json; charset=utf-8");
            Assert.Equal("asdf", content);
        }

        [Fact]
        public async Task ByteArrayJsonResponse()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            var content = await Response(bytes, "application/json; charset=utf-8");
            Assert.Equal("\"" + base64 + "\"", content);
        }

        [Fact]
        public async Task ByteArrayJsonReturn()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            var content = await Return(bytes, "application/json; charset=utf-8");
            Assert.Equal("\"" + base64 + "\"", content);
        }

        [Fact]
        public async Task ByteArrayJsonRaw()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            var content = await Raw(bytes, "application/json; charset=utf-8");
            Assert.Equal("asdf", content);
        }

        [Fact]
        public async Task ObjectJsonResponse()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            var content = await Response(obj, "application/json; charset=utf-8");
            content = Regex.Replace(content, @"\s+", string.Empty);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ObjectJsonReturn()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            var content = await Return(obj, "application/json; charset=utf-8");
            content = Regex.Replace(content, @"\s+", string.Empty);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ObjectJsonRaw()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            var content = await Raw(obj, "application/json; charset=utf-8");
            content = Regex.Replace(content, @"\s+", string.Empty);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task StringXmlResponse()
        {
            var content = await Response("asdf", "application/xml; charset=utf-8");
            Assert.Equal("<string xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">asdf</string>", content);
        }

        [Fact]
        public async Task StringXmlReturn()
        {
            var content = await Return("asdf", "application/xml; charset=utf-8");
            Assert.Equal("<string xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">asdf</string>", content);
        }

        [Fact]
        public async Task StringXmlRaw()
        {
            var content = await Raw("asdf", "application/xml; charset=utf-8");
            Assert.Equal("asdf", content);
        }

        [Fact]
        public async Task ByteArrayXmlResponse()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            var content = await Response(bytes, "application/xml; charset=utf-8");
            Assert.Equal("<base64Binary xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">YXNkZg==</base64Binary>", content);
        }

        [Fact]
        public async Task ByteArrayXmlReturn()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            var content = await Return(bytes, "application/xml; charset=utf-8");
            Assert.Equal("<base64Binary xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">YXNkZg==</base64Binary>", content);
        }

        [Fact]
        public async Task ByteArrayXmlRaw()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            var content = await Raw(bytes, "application/xml; charset=utf-8");
            Assert.Equal("asdf", content);
        }

        [Fact]
        public async Task ObjectXmlResponse()
        {
            var obj = new { a = 1 };

            // consider using fabiocav custom xml formatter
            var str = "<ArrayOfKeyValueOfstringanyTypexmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\"xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/Arrays\"><KeyValueOfstringanyType><Key>a</Key><Valuexmlns:d3p1=\"http://www.w3.org/2001/XMLSchema\"i:type=\"d3p1:int\">1</Value></KeyValueOfstringanyType></ArrayOfKeyValueOfstringanyType>";
            var content = await Response(obj, "application/xml; charset=utf-8");
            content = Regex.Replace(content, @"\s+", string.Empty);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ObjectXmlReturn()
        {
            var obj = new { a = 1 };

            // consider using fabiocav custom xml formatter
            var str = "<ArrayOfKeyValueOfstringanyTypexmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\"xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/Arrays\"><KeyValueOfstringanyType><Key>a</Key><Valuexmlns:d3p1=\"http://www.w3.org/2001/XMLSchema\"i:type=\"d3p1:int\">1</Value></KeyValueOfstringanyType></ArrayOfKeyValueOfstringanyType>";
            var content = await Return(obj, "application/xml; charset=utf-8");
            content = Regex.Replace(content, @"\s+", string.Empty);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ObjectXmlRaw()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            var content = await Raw(obj, "application/xml; charset=utf-8");
            content = Regex.Replace(content, @"\s+", string.Empty);
            Assert.Equal(str, content);
        }

        protected Task<string> Response<Req>(Req content, string contentType, string expectedContentType = null)
        {
            return CreateTest(content, contentType, false, false, expectedContentType);
        }

        protected Task<string> Return<Req>(Req content, string contentType, string expectedContentType = null)
        {
            return CreateTest(content, contentType, false, true, expectedContentType);
        }

        protected Task<string> Raw<Req>(Req content, string contentType, string expectedContentType = null)
        {
            return CreateTest(content, contentType, true, false, expectedContentType);
        }

        protected async Task<string> CreateTest<Req>(Req content, string contentType, bool isRaw, bool isReturn, string expectedContentType = null)
        {
            HttpContent reqContent;
            if (content is byte[])
            {
                reqContent = new ByteArrayContent(content as byte[]);
                reqContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            }
            else if (content is string)
            {
                reqContent = new StringContent(content as string);
            }
            else
            {
                reqContent = new ObjectContent(typeof(Req), content, new JsonMediaTypeFormatter());
                reqContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                // spoof CreateRequestObject in NodeFunctionInvoker
                reqContent.Headers.ContentLength = 1;
            }

            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger")),
                Method = HttpMethod.Post,
                Content = reqContent
            };
            request.Headers.Add("accept", contentType);
            request.Headers.Add("type", contentType);
            request.Headers.Add("scenario", "content");
            if (isRaw)
            {
                request.Headers.Add("raw", "true");
            }
            if (isReturn)
            {
                request.Headers.Add("return", "true");
            }
            request.SetConfiguration(Fixture.RequestConfiguration);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-Scenarios", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            MediaTypeHeaderValue expected = null;
            MediaTypeHeaderValue.TryParse(expectedContentType ?? contentType, out expected);
            Assert.Equal(expected, response.Content.Headers.ContentType);
            return await response.Content.ReadAsStringAsync();
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Node", "node")
            {
            }
        }
    }
}