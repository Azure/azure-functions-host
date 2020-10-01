﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.Tests.HttpWorker;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class HttpRequestExtensionsTest
    {
        [Fact]
        public void IsAppServiceInternalRequest_ReturnsExpectedResult()
        {
            // not running under Azure
            var request = HttpTestHelpers.CreateHttpRequest("GET", "http://foobar");
            Assert.False(request.IsAppServiceInternalRequest());

            // running under Azure
            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteInstanceId, "123" }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                // with header
                var headers = new HeaderDictionary();
                headers.Add(ScriptConstants.AntaresLogIdHeaderName, "123");

                request = HttpTestHelpers.CreateHttpRequest("GET", "http://foobar", headers);
                Assert.False(request.IsAppServiceInternalRequest());

                request = HttpTestHelpers.CreateHttpRequest("GET", "http://foobar");
                Assert.True(request.IsAppServiceInternalRequest());
            }
        }

        [Fact]
        public void ConvertUserIdentitiesToJArray_RemovesCircularReference()
        {
            IIdentity identity = new TestIdentity();
            Claim claim = new Claim("authlevel", "admin", "test", "LOCAL AUTHORITY", "LOCAL AUTHORITY");
            List<Claim> claims = new List<Claim>() { claim };
            ClaimsIdentity claimsIdentity = new ClaimsIdentity(identity, claims);
            List<ClaimsIdentity> claimsIdentities = new List<ClaimsIdentity>() { claimsIdentity };
            var userIdentitiesString = HttpRequestExtensions.GetUserIdentitiesAsJArray(claimsIdentities);
            Assert.Contains("TestAuthType", userIdentitiesString[0]["AuthenticationType"].ToString());
        }

        [Fact]
        public void GetQueryCollectionAsJObject_Expected()
        {
            var testHttpRequest = HttpWorkerTestUtilities.GetTestHttpRequest();
            var query = testHttpRequest.GetQueryCollectionAsJObject();
            Assert.Equal("Ink And Toner", query["name"]);
        }

        [Fact]
        public async Task GetHttpRequest_HasAllHeadersMethodAndBody()
        {
            string requestUri = "http://localhost";
            HttpRequest testRequest = HttpWorkerTestUtilities.GetTestHttpRequest();
            Uri expectedUri = new Uri(QueryHelpers.AddQueryString(requestUri, testRequest.GetQueryCollectionAsDictionary()));
            HttpRequestMessage clonedRequest = testRequest.ToHttpRequestMessage(requestUri);
            Assert.Equal(testRequest.Headers.Count, clonedRequest.Headers.Count() + 1); // Content-Length would go to content header

            foreach (var header in testRequest.Headers)
            {
                IEnumerable<string> actualHeaderValue = header.Value.AsEnumerable();
                IEnumerable<string> clonedHeaderValue;

                if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    clonedHeaderValue = clonedRequest.Headers.GetValues(header.Key);
                }
                else
                {
                    clonedHeaderValue = clonedRequest.Content.Headers.GetValues(header.Key);
                }

                var count = actualHeaderValue.Except(clonedHeaderValue).Count();
                Assert.Equal(count, 0);
            }

            Assert.Equal(testRequest.Method, clonedRequest.Method.ToString());
            Assert.Equal(clonedRequest.RequestUri.ToString(), expectedUri.ToString());
            Assert.Equal(await clonedRequest.Content.ReadAsStringAsync(), "\"hello world\"");
        }
    }

    internal class TestIdentity : IIdentity
    {
        public string AuthenticationType => "TestAuthType";

        public bool IsAuthenticated => true;

        public string Name => "TestIdentityName";
    }
}
