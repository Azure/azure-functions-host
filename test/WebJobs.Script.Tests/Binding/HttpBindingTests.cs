// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HttpBindingTests
    {
        [Fact]
        public void AddResponseHeader_ContentMD5_AddsExpectedHeader()
        {
            HttpResponseMessage response = new HttpResponseMessage()
            {
                Content = new StringContent("Test")
            };
            byte[] bytes = Encoding.UTF8.GetBytes("This is a test");
            var header = new KeyValuePair<string, object>("content-md5", bytes);
            HttpBinding.AddResponseHeader(response, header);
            Assert.Equal(bytes, response.Content.Headers.ContentMD5);

            response = new HttpResponseMessage()
            {
                Content = new StringContent("Test")
            };
            string base64 = Convert.ToBase64String(bytes);
            header = new KeyValuePair<string, object>("content-md5", base64);
            HttpBinding.AddResponseHeader(response, header);
            Assert.Equal(base64, Convert.ToBase64String(response.Content.Headers.ContentMD5));
        }

        [Fact]
        public void ParseResponseObject_ReturnsExpectedResult()
        {
            IDictionary<string, object> inputHeaders = new Dictionary<string, object>()
            {
                { "content-type", "text/plain" }
            };

            dynamic responseObject = new ExpandoObject();
            responseObject.body = "Test Body";
            responseObject.headers = inputHeaders;
            responseObject.status = 202;
            responseObject.isRaw = false;

            object content = null;
            IDictionary<string, object> headers = null;
            HttpStatusCode statusCode = HttpStatusCode.OK;
            bool isRawResponse = false;
            HttpBinding.ParseResponseObject(responseObject, ref content, out headers, out statusCode, out isRawResponse);

            Assert.Equal("Test Body", content);
            Assert.Same(headers, headers);
            Assert.Equal(HttpStatusCode.Accepted, statusCode);
            Assert.False(isRawResponse);

            // verify case insensitivity
            responseObject = new ExpandoObject();
            responseObject.Body = "Test Body";
            responseObject.Headers = inputHeaders;
            responseObject.StatusCode = "202";  // verify string works as well
            responseObject.Status = "404"; // verify that StatusCode takes precidence over Status if both are specified
            responseObject.isRaw = true;

            content = null;
            headers = null;
            statusCode = HttpStatusCode.OK;
            isRawResponse = false;
            HttpBinding.ParseResponseObject(responseObject, ref content, out headers, out statusCode, out isRawResponse);

            Assert.Equal("Test Body", content);
            Assert.Same(headers, headers);
            Assert.Equal(HttpStatusCode.Accepted, statusCode);
            Assert.True(isRawResponse);
        }

        [Fact]
        public void ParseResponseObject_StatusWithNullBody_ReturnsExpectedResult()
        {
            dynamic responseObject = new ExpandoObject();
            responseObject.body = null;
            responseObject.status = 202;

            object content = null;
            IDictionary<string, object> headers = null;
            HttpStatusCode statusCode = HttpStatusCode.OK;
            bool isRawResponse = false;
            HttpBinding.ParseResponseObject(responseObject, ref content, out headers, out statusCode, out isRawResponse);

            Assert.Equal(null, content);
            Assert.Equal(HttpStatusCode.Accepted, statusCode);
        }

        [Fact]
        public async Task CreateResultContent_ExpandoObject_ReturnsJsonStringContent()
        {
            dynamic expandoObject = new ExpandoObject();
            expandoObject.name = "Mathew";
            expandoObject.location = "Seattle";

            StringContent stringContent = HttpBinding.CreateResultContent(expandoObject);
            string json = await stringContent.ReadAsStringAsync();
            JObject parsed = JObject.Parse(json);
            Assert.Equal("Mathew", parsed["name"]);
            Assert.Equal("Seattle", parsed["location"]);
            Assert.Equal("text/plain", stringContent.Headers.ContentType.MediaType);

            stringContent = HttpBinding.CreateResultContent(expandoObject, "application/json");
            json = await stringContent.ReadAsStringAsync();
            parsed = JObject.Parse(json);
            Assert.Equal("Mathew", parsed["name"]);
            Assert.Equal("Seattle", parsed["location"]);
            Assert.Equal("application/json", stringContent.Headers.ContentType.MediaType);
        }

        [Fact]
        public async Task CreateResponse_JsonString_ReturnsExpectedResult()
        {
            HttpConfiguration config = new HttpConfiguration();
            config.Formatters.Add(new PlaintextMediaTypeFormatter());

            JObject child = new JObject
            {
                { "Name", "Mary" },
                { "Location", "Seattle" },
                { "Age", 5 }
            };

            JObject parent = new JObject
            {
                { "Name", "Bob" },
                { "Location", "Seattle" },
                { "Age", 40 },
                { "Children", new JArray(child) }
            };
            string expectedBodyJson = parent.ToString(Formatting.None);

            // explicitly set a content type that there is no default
            // formatter for to force default non-negotiated content codepath
            JObject headers = new JObject
            {
                { "Content-Type", "foo/bar" }
            };
            JObject responseObject = new JObject
            {
                { "Body", parent },
                { "Headers", headers }
            };
            HttpRequestMessage request = new HttpRequestMessage();
            request.SetConfiguration(config);
            var response = HttpBinding.CreateResponse(request, responseObject.ToString());
            string resultJson = await response.Content.ReadAsStringAsync();
            Assert.Equal(expectedBodyJson, resultJson);
            Assert.Equal("foo/bar", response.Content.Headers.ContentType.MediaType);

            // Test again with a recognized content-type header, to force content negotiation
            headers = new JObject
            {
                { "Content-Type", "application/json" }
            };
            responseObject = new JObject
            {
                { "Body", parent },
                { "Headers", headers }
            };
            request = new HttpRequestMessage();
            request.SetConfiguration(config);
            response = HttpBinding.CreateResponse(request, responseObject.ToString());
            resultJson = await response.Content.ReadAsStringAsync();
            Assert.Equal(expectedBodyJson, resultJson);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);

            // Test again with an explicitly specified response content type
            headers = new JObject
            {
                { "Content-Type", "text/plain" }
            };
            responseObject = new JObject
            {
                { "Body", parent },
                { "Headers", headers }
            };
            request = new HttpRequestMessage();
            request.SetConfiguration(config);
            response = HttpBinding.CreateResponse(request, responseObject.ToString());
            resultJson = await response.Content.ReadAsStringAsync();
            Assert.Equal(expectedBodyJson, resultJson);
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);

            // Test again without an explicit response content type
            // to trigger ObjectContent negotiation codepath
            responseObject = new JObject
            {
                { "Body", parent }
            };
            request = new HttpRequestMessage();
            request.SetConfiguration(config);
            response = HttpBinding.CreateResponse(request, responseObject.ToString());
            resultJson = await response.Content.ReadAsStringAsync();
            Assert.Equal(expectedBodyJson, resultJson);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
        }
    }
}
