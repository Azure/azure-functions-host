// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Binding;
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
            responseObject.enableContentNegotiation = false;

            object content = null;
            int statusCode = StatusCodes.Status200OK;
            List<Tuple<string, string, CookieOptions>> cookies = null;
            HttpBinding.ParseResponseObject(responseObject, ref content, out IDictionary<string, object> headers, out statusCode, out cookies, out bool enableContentNegotiationResponse);

            Assert.Equal("Test Body", content);
            Assert.Same(headers, headers);
            Assert.Equal(StatusCodes.Status202Accepted, statusCode);
            Assert.False(enableContentNegotiationResponse);
            // No cookies found or set
            Assert.True(cookies == null || !cookies.Any());

            // verify case insensitivity
            responseObject = new ExpandoObject();
            responseObject.Body = "Test Body";
            responseObject.Headers = inputHeaders;
            responseObject.StatusCode = "202";  // verify string works as well
            responseObject.Status = "404"; // verify that StatusCode takes precidence over Status if both are specified
            responseObject.enableContentNegotiation = true;

            content = null;
            headers = null;
            statusCode = StatusCodes.Status200OK;
            enableContentNegotiationResponse = false;
            HttpBinding.ParseResponseObject(responseObject, ref content, out headers, out statusCode, out cookies, out enableContentNegotiationResponse);

            Assert.Equal("Test Body", content);
            Assert.Same(headers, headers);
            Assert.Equal(StatusCodes.Status202Accepted, statusCode);
            Assert.True(enableContentNegotiationResponse);
            // No cookies found or set
            Assert.True(cookies == null || !cookies.Any());
        }

        [Fact]
        public void ParseResponseObject_WithCookies_ReturnsExpectedResult()
        {
            var cookieProperties = new Tuple<string, string, CookieOptions>("hello", "world", new CookieOptions()
            {
                Domain = "/",
                MaxAge = TimeSpan.FromSeconds(60),
                HttpOnly = true
            });

            IList<Tuple<string, string, CookieOptions>> cookieContents = new List<Tuple<string, string, CookieOptions>>()
            {
                cookieProperties
            };

            dynamic responseObject = new ExpandoObject();
            responseObject.Body = "Test Body";
            responseObject.Cookies = cookieContents;
            responseObject.StatusCode = "202";  // verify string works as well

            object content = null;
            var statusCode = StatusCodes.Status200OK;
            HttpBinding.ParseResponseObject(responseObject, ref content, out IDictionary<string, object> headers, out statusCode, out List<Tuple<string, string, CookieOptions>> cookies, out bool enableContentNegotiationResponse);

            Assert.Equal("Test Body", content);
            Assert.Same(cookieContents, cookies);
            Assert.Equal(cookieContents.Count, cookies.Count);
            var firstCookie = cookies.First();
            Assert.Same(cookieProperties, firstCookie);
            Assert.Same(cookieProperties.Item1, firstCookie.Item1);
            Assert.Same(cookieProperties.Item2, firstCookie.Item2);
            Assert.Same(cookieProperties.Item3, firstCookie.Item3);
            Assert.Equal(StatusCodes.Status202Accepted, statusCode);
            Assert.False(enableContentNegotiationResponse);
            Assert.Null(headers);
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
            bool enableContentNegotiationResponse = false;
            HttpBinding.ParseResponseObject(responseObject, ref content, out headers, out statusCode, out List<Tuple<string, string, CookieOptions>> cookies, out enableContentNegotiationResponse);

            Assert.Null(content);
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

        [Fact]
        public void SetResponse_ActionResultConverted_Succeeded()
        {
            FieldInfo fieldInfo = typeof(HttpBinding).GetField("isActionResultHandlingEnabled", BindingFlags.NonPublic | BindingFlags.Static);
            bool oldValue = (bool)fieldInfo.GetValue(null);
            fieldInfo.SetValue(null, true);

            try
            {
                var httpContext1 = new DefaultHttpContext();
                ActionResult<string> result1 = new ActionResult<string>("test");
                HttpBinding.SetResponse(httpContext1.Request, result1);
                Assert.Equal("test", ((ObjectResult)httpContext1.Request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey]).Value);

                var httpContext2 = new DefaultHttpContext();
                ActionResult<DummyClass> result2 = new ActionResult<DummyClass>(new DummyClass { Value = "test" });
                HttpBinding.SetResponse(httpContext2.Request, result2);
                var resultObject = ((ObjectResult)httpContext2.Request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey]).Value;
                Assert.IsType<DummyClass>(resultObject);
                Assert.Equal("test", ((DummyClass)resultObject).Value);
            }
            finally
            {
                fieldInfo.SetValue(null, oldValue);
            }
        }

        [Fact]
        public void SetResponse_ActionResultGeneric_Succeeded()
        {
            var httpContext1 = new DefaultHttpContext();
            ActionResult<string> result1 = new ActionResult<string>("test");
            HttpBinding.SetResponse(httpContext1.Request, result1);
            var resultObject1 = ((ObjectResult)httpContext1.Request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey]).Value;
            Assert.IsType<ActionResult<string>>(resultObject1);
            Assert.Equal("test", (resultObject1 as ActionResult<string>).Value);

            var httpContext2 = new DefaultHttpContext();
            ActionResult<DummyClass> result = new ActionResult<DummyClass>(new DummyClass { Value = "test" });
            HttpBinding.SetResponse(httpContext2.Request, result);
            var resultObject2 = ((ObjectResult)httpContext2.Request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey]).Value;
            Assert.IsType<ActionResult<DummyClass>>(resultObject2);
            Assert.Equal("test", (resultObject2 as ActionResult<DummyClass>).Value.Value);
        }

        private class DummyClass
        {
            public string Value { get; set; }
        }
    }
}