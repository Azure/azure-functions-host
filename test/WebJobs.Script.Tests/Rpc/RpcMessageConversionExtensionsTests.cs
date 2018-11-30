// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class RpcMessageConversionExtensionsTests
    {
        [Theory]
        [InlineData("application/x-www-form-urlencoded’", "say=Hi&to=Mom")]
        public void HttpObjects_StringBody(string expectedContentType, object body)
        {
            var headers = new HeaderDictionary();
            headers.Add("content-type", expectedContentType);
            HttpRequest request = HttpTestHelpers.CreateHttpRequest("GET", "http://localhost/api/httptrigger-scenarios", headers, body);

            var rpcRequestObject = request.ToRpc();
            Assert.Equal(body.ToString(), rpcRequestObject.Http.Body.String);

            string contentType;
            rpcRequestObject.Http.Headers.TryGetValue("content-type", out contentType);
            Assert.Equal(expectedContentType, contentType);
        }

        [Theory]
        [InlineData("say=Hi&to=Mom", new string[] { "say", "to" }, new string[] { "Hi", "Mom" })]
        [InlineData("say=Hi", new string[] { "say" }, new string[] { "Hi" })]
        [InlineData("say=Hi&to=", new string[] { "say" }, new string[] { "Hi" })] // Removes empty value query params
        public void HttpObjects_Query(string queryString, string[] expectedKeys, string[] expectedValues)
        {
            HttpRequest request = HttpTestHelpers.CreateHttpRequest("GET", $"http://localhost/api/httptrigger-scenarios?{queryString}");

            var rpcRequestObject = request.ToRpc();
            // Same number of expected key value pairs
            Assert.Equal(rpcRequestObject.Http.Query.Count, expectedKeys.Length);
            Assert.Equal(rpcRequestObject.Http.Query.Count, expectedValues.Length);
            // Same key and value strings for each pair
            for (int i = 0; i < expectedKeys.Length; i++)
            {
                Assert.True(rpcRequestObject.Http.Query.ContainsKey(expectedKeys[i]));
                Assert.Equal(rpcRequestObject.Http.Query.GetValueOrDefault(expectedKeys[i]), expectedValues[i]);
            }
        }
    }
}
