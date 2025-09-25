// Copyright (c) .NET Foundation. All rights reserved.
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
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class HttpRequestExtensionsTest
    {
        [Theory]
        [Trait(TestTraits.Group, TestTraits.AdminIsolationTests)]
        [InlineData(ScriptConstants.DynamicSku, null, "True", false)]
        [InlineData(ScriptConstants.DynamicSku, "1", "True", true)]
        [InlineData(ScriptConstants.DynamicSku, "1", "true", true)]
        [InlineData(ScriptConstants.DynamicSku, "1", "False", false)]
        [InlineData(ScriptConstants.DynamicSku, "1", null, false)]
        [InlineData(ScriptConstants.FlexConsumptionSku, null, "True", true)]
        [InlineData(ScriptConstants.FlexConsumptionSku, null, null, false)]
        public void IsPlatformInternalRequest_ReturnsExpectedResult(string sku, string websiteInstanceId, string platformInternalHeaderValue, bool expected)
        {
            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteInstanceId, websiteInstanceId },
                { EnvironmentSettingNames.AzureWebsiteSku, sku }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                var environment = SystemEnvironment.Instance;

                var headers = new HeaderDictionary();
                headers.Add(ScriptConstants.AntaresPlatformInternal, platformInternalHeaderValue);

                // verify using explicit environment
                var request = HttpTestHelpers.CreateHttpRequest("GET", "http://foobar", headers);
                Assert.Equal(expected, request.IsPlatformInternalRequest(environment));

                // verify using request services environment
                request = HttpTestHelpers.CreateHttpRequest("GET", "http://foobar", headers);
                var servicesMock = new Mock<IServiceProvider>();
                servicesMock.Setup(s => s.GetService(typeof(IEnvironment))).Returns(environment);
                request.HttpContext.RequestServices = servicesMock.Object;
                Assert.Equal(expected, request.IsPlatformInternalRequest());
            }
        }

        [Theory]
        [InlineData(ScriptConstants.DynamicSku, true, true, false)]
        [InlineData(ScriptConstants.DynamicSku, true, false, true)]
        [InlineData(ScriptConstants.DynamicSku, false, true, false)]
        [InlineData(ScriptConstants.DynamicSku, false, false, false)]
        [InlineData(ScriptConstants.FlexConsumptionSku, false, true, false)]
        [InlineData(ScriptConstants.FlexConsumptionSku, false, false, true)]
        public void IsAppServiceInternalRequest_ReturnsExpectedResult(string sku, bool includeWebsiteInstanceId, bool includeLogIdHeader, bool expected)
        {
            var request = HttpTestHelpers.CreateHttpRequest("GET", "http://foobar");

            var vars = new Dictionary<string, string>();
            vars.Add(EnvironmentSettingNames.AzureWebsiteSku, sku);
            if (includeWebsiteInstanceId)
            {
                // container running in App Service
                vars.Add(EnvironmentSettingNames.AzureWebsiteInstanceId, "123");
            }

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                var headers = new HeaderDictionary();

                if (includeLogIdHeader)
                {
                    // include the FE log id header
                    headers.Add(ScriptConstants.AntaresLogIdHeaderName, "123");
                }

                request = HttpTestHelpers.CreateHttpRequest("GET", "http://foobar", headers);
                Assert.Equal(expected, request.IsAppServiceInternalRequest());
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
