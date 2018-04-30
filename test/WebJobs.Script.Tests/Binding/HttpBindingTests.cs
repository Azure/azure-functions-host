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
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HttpBindingTests
    {
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
            int statusCode = StatusCodes.Status200OK;
            HttpBinding.ParseResponseObject(responseObject, ref content, out IDictionary<string, object> headers, out statusCode, out bool isRawResponse);

            Assert.Equal("Test Body", content);
            Assert.Same(headers, headers);
            Assert.Equal(StatusCodes.Status202Accepted, statusCode);
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
            statusCode = StatusCodes.Status200OK;
            isRawResponse = false;
            HttpBinding.ParseResponseObject(responseObject, ref content, out headers, out statusCode, out isRawResponse);

            Assert.Equal("Test Body", content);
            Assert.Same(headers, headers);
            Assert.Equal(StatusCodes.Status202Accepted, statusCode);
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
            int statusCode = StatusCodes.Status200OK;
            bool isRawResponse = false;
            HttpBinding.ParseResponseObject(responseObject, ref content, out headers, out statusCode, out isRawResponse);

            Assert.Equal(null, content);
            Assert.Equal(StatusCodes.Status202Accepted, statusCode);
        }

        [Theory]
        [InlineData("status", (short)301, StatusCodes.Status301MovedPermanently, true)]
        [InlineData("status", (ushort)401, StatusCodes.Status401Unauthorized, true)]
        [InlineData("status", (int)501, StatusCodes.Status501NotImplemented, true)]
        [InlineData("status", (uint)202, StatusCodes.Status202Accepted, true)]
        [InlineData("status", (long)302, StatusCodes.Status302Found, true)]
        [InlineData("status", (ulong)402, StatusCodes.Status402PaymentRequired, true)]
        [InlineData("status", StatusCodes.Status409Conflict, StatusCodes.Status409Conflict, true)]
        [InlineData("statusCode", (int)202, StatusCodes.Status202Accepted, true)]
        [InlineData("statusCode", "202", StatusCodes.Status202Accepted, true)]
        [InlineData("statusCode", "invalid", StatusCodes.Status202Accepted, false)]
        [InlineData("statusCode", "", StatusCodes.Status202Accepted, false)]
        [InlineData("statusCode", null, StatusCodes.Status202Accepted, false)]
        [InlineData("code", (int)202, StatusCodes.Status202Accepted, false)]
        public void TryParseStatusCode_ReturnsExpectedResult(string propertyName, object value, int expectedStatusCode, bool expectedReturn)
        {
            var responseObject = new Dictionary<string, object>
            {
                { propertyName, value }
            };

            bool returnValue = HttpBinding.TryParseStatusCode(responseObject, out int? statusCode);

            Assert.Equal(expectedReturn, returnValue);
            if (expectedReturn)
            {
                Assert.Equal(expectedStatusCode, statusCode);
            }
        }
    }
}