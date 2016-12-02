// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class NodeTextPlain : ContentTests
    {
        public NodeTextPlain(ContentFixture fixture) : base(fixture, "Node", "text/plain; charset=utf-8")
        {
        }

        [Fact]
        public async Task StringResponse()
        {
            var str = "asdf";
            await Response(str);
            Assert.Equal(str, await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task StringReturn()
        {
            var str = "asdf";
            await Return(str);
            Assert.Equal(str, await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task StringRaw()
        {
            var str = "asdf";
            await Raw(str);
            Assert.Equal(str, await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ByteArrayResponse()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            await Response(bytes);
            Assert.Equal(str, await Content.ReadAsStringAsync());
        }

        // consider supporting text/plain formatting for byte[] type
        [Fact]
        public async Task ByteArrayReturn()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            await CreateTest(bytes, false, true, "application/json; charset=utf-8");
            Assert.Equal("\"" + base64 + "\"", await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ByteArrayRaw()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            await Raw(bytes);
            Assert.Equal(str, await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ObjectResponse()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            await Response(obj);
            Assert.Equal(str, Regex.Replace(await Content.ReadAsStringAsync(), @"\s+", String.Empty));
        }

        // consider supporting text/plain conversion for expandoobject type
        [Fact]
        public async Task ObjectReturn()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            await CreateTest(obj, false, true, "application/json; charset=utf-8");
            Assert.Equal(str, Regex.Replace(await Content.ReadAsStringAsync(), @"\s+", String.Empty));
        }

        [Fact]
        public async Task ObjectRaw()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            await Raw(obj);
            Assert.Equal(str, Regex.Replace(await Content.ReadAsStringAsync(), @"\s+", String.Empty));
        }
    }

    public class NodeApplicationJson : ContentTests
    {
        public NodeApplicationJson(ContentFixture fixture) : base(fixture, "Node", "application/json; charset=utf-8")
        {
        }

        [Fact]
        public async Task StringResponse()
        {
            await Response("asdf");
            Assert.Equal("\"asdf\"", await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task StringReturn()
        {
            await Return("asdf");
            Assert.Equal("\"asdf\"", await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task StringRaw()
        {
            await Raw("asdf");
            Assert.Equal("asdf", await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ByteArrayResponse()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            await Response(bytes);
            Assert.Equal("\"" + base64 + "\"", await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ByteArrayReturn()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            await Return(bytes);
            Assert.Equal("\"" + base64 + "\"", await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ByteArrayRaw()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            await Raw(bytes);
            Assert.Equal("asdf", await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ObjectResponse()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            await Response(obj);
            var content = Regex.Replace(await Content.ReadAsStringAsync(), @"\s+", String.Empty);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ObjectReturn()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            await Return(obj);
            var content = Regex.Replace(await Content.ReadAsStringAsync(), @"\s+", String.Empty);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ObjectRaw()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            await Raw(obj);
            var content = Regex.Replace(await Content.ReadAsStringAsync(), @"\s+", String.Empty);
            Assert.Equal(str, content);
        }
    }
    public class NodeApplicationXml : ContentTests
    {
        public NodeApplicationXml(ContentFixture fixture) : base(fixture, "Node", "application/xml; charset=utf-8")
        {
        }

        [Fact]
        public async Task StringResponse()
        {
            await Response("asdf");
            var content = await Content.ReadAsStringAsync();
            Assert.Equal("<string xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">asdf</string>", content);
        }

        [Fact]
        public async Task StringReturn()
        {
            await Return("asdf");
            var content = await Content.ReadAsStringAsync();
            Assert.Equal("<string xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">asdf</string>", content);
        }

        [Fact]
        public async Task StringRaw()
        {
            await Raw("asdf");
            Assert.Equal("asdf", await Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ByteArrayResponse()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            await Response(bytes);
            var content = await Content.ReadAsStringAsync();
            Assert.Equal("<base64Binary xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">YXNkZg==</base64Binary>", content);
        }

        [Fact]
        public async Task ByteArrayReturn()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            await Return(bytes);
            var content = await Content.ReadAsStringAsync();
            Assert.Equal("<base64Binary xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">YXNkZg==</base64Binary>", content);
        }

        [Fact]
        public async Task ByteArrayRaw()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            await Raw(bytes);
            var content = await Content.ReadAsStringAsync();
            Assert.Equal("asdf", content);
        }

        [Fact]
        public async Task ObjectResponse()
        {
            var obj = new { a = 1 };
            // consider using fabiocav custom xml formatter
            var str = "<ArrayOfKeyValueOfstringanyTypexmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\"xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/Arrays\"><KeyValueOfstringanyType><Key>a</Key><Valuexmlns:d3p1=\"http://www.w3.org/2001/XMLSchema\"i:type=\"d3p1:int\">1</Value></KeyValueOfstringanyType></ArrayOfKeyValueOfstringanyType>";
            await Response(obj);
            var content = Regex.Replace(await Content.ReadAsStringAsync(), @"\s+", String.Empty);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ObjectReturn()
        {
            var obj = new { a = 1 };
            // consider using fabiocav custom xml formatter
            var str = "<ArrayOfKeyValueOfstringanyTypexmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\"xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/Arrays\"><KeyValueOfstringanyType><Key>a</Key><Valuexmlns:d3p1=\"http://www.w3.org/2001/XMLSchema\"i:type=\"d3p1:int\">1</Value></KeyValueOfstringanyType></ArrayOfKeyValueOfstringanyType>";
            await Return(obj);
            var content = Regex.Replace(await Content.ReadAsStringAsync(), @"\s+", String.Empty);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ObjectRaw()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            await Raw(obj);
            var content = Regex.Replace(await Content.ReadAsStringAsync(), @"\s+", String.Empty);
            Assert.Equal(str, content);
        }
    }
    
    public class ContentTests : IClassFixture<ContentFixture>
    {
        public ContentTests(ContentFixture fixture, string lang, string contentType)
        {
            Fixture = fixture;
            Language = lang;
            ContentType = contentType;
        }

        protected ContentFixture Fixture { get; private set; }
        protected HttpContent Content { get; private set; }
        protected string ContentType { get; private set; }
        protected string Language { get; private set; }

        protected Task Response<Req>(Req content)
        {
            return CreateTest(content, false, false);
        }

        protected Task Return<Req>(Req content)
        {
            return CreateTest(content, false, true);
        }

        protected Task Raw<Req>(Req content)
        {
            return CreateTest(content, true, false);
        }

        protected async Task CreateTest<Req>(Req content, bool isRaw, bool isReturn, string expectedContentType = null)
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
            request.Headers.Add("accept", ContentType);

            request.Headers.Add("type", ContentType);
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
                { "request", request }
            };
            await Fixture.Host.CallAsync(Language, arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            MediaTypeHeaderValue expected = null;
            MediaTypeHeaderValue.TryParse(expectedContentType ?? ContentType, out expected);
            Assert.Equal(expected, response.Content.Headers.ContentType);
            Content = response.Content;
        }
    }

    public class ContentFixture : IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;

        public ContentFixture()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            FixtureId = "content";

            TraceWriter = new TestTraceWriter(TraceLevel.Verbose);

            ApiHubTestHelper.SetDefaultConnectionFactory();

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = "TestScripts/Content",
                TraceWriter = TraceWriter,
                FileLoggingMode = FileLoggingMode.Always,
                HostConfig = new JobHostConfiguration()
                {
                    HostId = "content"
                }
            };

            RequestConfiguration = new HttpConfiguration();
            RequestConfiguration.Formatters.Add(new PlaintextMediaTypeFormatter());

            Host = ScriptHost.Create(_settingsManager, config);
            Host.Start();
        }

        public TestTraceWriter TraceWriter { get; private set; }

        public ScriptHost Host { get; private set; }

        public string FixtureId { get; private set; }

        public HttpConfiguration RequestConfiguration { get; }

        public void Dispose()
        {
            Host.Stop();
            Host.Dispose();
        }
    }
}
