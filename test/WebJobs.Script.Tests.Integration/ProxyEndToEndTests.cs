// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ProxyEndToEndTests : EndToEndTestsBase<ProxyEndToEndTests.TestFixture>
    {
        public ProxyEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task Proxy_Mock_UrlTemplate()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/mymockhttp")),
                Method = HttpMethod.Get,
            };

            request.SetConfiguration(Fixture.RequestConfiguration);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request },
            };
            await Fixture.Host.CallAsync("localFunction_1002", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(response.Headers.GetValues("myversion").ToArray()[0], "123");
        }

        public class TestFixture : EndToEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\Proxy";

            public TestFixture() : base(ScriptRoot, "proxy")
            {
            }
        }
    }
}
