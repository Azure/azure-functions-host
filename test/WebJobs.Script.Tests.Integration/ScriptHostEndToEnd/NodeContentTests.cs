// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class NodeContentTests : IClassFixture<NodeContentTests.TestFixture>
    {
        private IWebHostLanguageWorkerChannelManager _languageWorkerChannelManager;

        public NodeContentTests(TestFixture fixture)
        {
            Fixture = fixture;
            _languageWorkerChannelManager = (IWebHostLanguageWorkerChannelManager)fixture.Host.Services.GetService(typeof(IWebHostLanguageWorkerChannelManager));
        }

        public TestFixture Fixture { get; set; }

        [Fact]
        public async Task StringTextPlainResponse_Conneg()
        {
            var str = "asdf";
            var content = await ResponseWithConneg(str, "text/plain; charset=utf-8");
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task StringTextPlain()
        {
            var str = "asdf";
            var content = await Response(str, "text/plain; charset=utf-8");
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task NullContentType_Ignored_Conneg()
        {
            var str = "asdf";
            var content = await ResponseWithConneg("asdf", null);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ByteArrayTextPlainResponse_Conneg()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var content = await ResponseWithConneg(bytes, "text/plain; charset=utf-8");
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ByteArrayTextPlain()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var content = await Response(bytes, "text/plain; charset=utf-8");
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task ObjectTextPlain()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            var content = await Response(obj, "text/plain; charset=utf-8");
            Assert.Equal(str, Regex.Replace(content, @"\s+", string.Empty));
        }

        [Fact]
        public async Task StringJson()
        {
            var content = await Response("asdf", "application/json; charset=utf-8");
            Assert.Equal("asdf", content);
        }

        [Fact]
        public async Task ByteArrayJson()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            var content = await Response(bytes, "application/json; charset=utf-8");
            Assert.Equal("asdf", content);
        }

        [Fact]
        public async Task ObjectJson()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            var content = await Response(obj, "application/json; charset=utf-8");
            content = Regex.Replace(content, @"\s+", string.Empty);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task StringXml()
        {
            var content = await Response("asdf", "application/xml; charset=utf-8");
            Assert.Equal("asdf", content);
        }

        [Fact]
        public async Task ByteArrayXmlResponse()
        {
            var str = "asdf";
            var bytes = Encoding.UTF8.GetBytes(str);
            var base64 = Convert.ToBase64String(bytes);
            var content = await Response(bytes, "application/xml; charset=utf-8");
            Assert.Equal("asdf", content);
        }

        [Fact]
        public async Task ObjectXml()
        {
            var obj = new { a = 1 };
            var str = "{\"a\":1}";
            var content = await Response(obj, "application/xml; charset=utf-8");
            content = Regex.Replace(content, @"\s+", string.Empty);
            Assert.Equal(str, content);
        }

        [Fact]
        public async Task InitializeAsync_WorkerRuntime_Node_DoNotInitialize_JavaWorker()
        {
            var channelManager = _languageWorkerChannelManager as WebHostLanguageWorkerChannelManager;

            ILanguageWorkerChannel javaChannel = await channelManager.GetChannelAsync(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(javaChannel);
            ILanguageWorkerChannel nodeChannel = await channelManager.GetChannelAsync(LanguageWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(nodeChannel);
        }

        // Get response with default ObjectResult content negotiation enabled 
        protected Task<string> ResponseWithConneg<Req>(Req content, string contentType, string expectedContentType = null)
        {
            return CreateTest(content, contentType, true, expectedContentType);
        }

        protected Task<string> Response<Req>(Req content, string contentType, string expectedContentType = null)
        {
            return CreateTest(content, contentType, false, expectedContentType);
        }

        protected async Task<string> CreateTest<Req>(Req content, string contentType, bool contentNegotiation, string expectedContentType = null)
        {
            IHeaderDictionary headers = new HeaderDictionary();

            headers.Add("accept", contentType);
            headers.Add("type", contentType);

            if (contentNegotiation)
            {
                headers.Add("negotiation", "true");
            }

            headers.Add("scenario", "content");

            HttpRequest request = HttpTestHelpers.CreateHttpRequest("POST", "http://localhost/api/httptrigger", headers, content);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.JobHost.CallAsync("HttpTrigger-Scenarios", arguments);

            var result = (IActionResult)request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];

            if (contentNegotiation)
            {
                ObjectResult objResult = result as ObjectResult;
                Assert.NotNull(objResult);
                if (contentType == null)
                {
                    Assert.Equal(0, objResult.ContentTypes.Count);
                }
                else
                {
                    Assert.Equal(contentType, objResult.ContentTypes[0]);
                }
                Assert.Equal(200, objResult.StatusCode);
                if (content is byte[])
                {
                    Assert.Equal(System.Text.Encoding.UTF8.GetString(content as byte[]), objResult.Value);
                }
                else
                {
                    Assert.Equal(content.ToString(), objResult.Value);
                }
                return objResult.Value.ToString();
            }
            else
            {
                RawScriptResult rawResult = result as RawScriptResult;
                Assert.NotNull(rawResult);
                Assert.Equal(contentType, rawResult.Headers["content-type"].ToString());
                Assert.Equal(200, rawResult.StatusCode);
                return rawResult.Content.ToString();
            }
        }

        public class TestFixture : ScriptHostEndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Node", "node", LanguageWorkerConstants.NodeLanguageWorkerName,
                startHost: true, functions: new[] { "HttpTrigger", "HttpTrigger-Scenarios" })
            {
            }
        }
    }
}