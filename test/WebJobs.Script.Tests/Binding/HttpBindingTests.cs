// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Binding;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Binding
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
    }
}
