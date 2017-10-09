// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.Script
{
    public class ScriptFunctionInvokerBaseTests
    {
        [Fact]
        public void InitializeHttpRequestEnvironmentVariables_SetsExpectedVariables()
        {
            var environmentVariables = new Dictionary<string, string>();

            var headers = new HeaderDictionary();
            headers.Add("TEST-A", "a");
            headers.Add("TEST-B", "b");

            var request = HttpTestHelpers.CreateHttpRequest("GET", "http://test.com/test?a=1&b=2&b=3&c=4", headers);

            var routeData = new Dictionary<string, object>
            {
                { "a", 123 },
                { "b", 456 }
            };
            request.HttpContext.Items.Add(HttpExtensionConstants.AzureWebJobsHttpRouteDataKey, routeData);

            ScriptFunctionInvokerBase.InitializeHttpRequestEnvironmentVariables(environmentVariables, request);
            Assert.Equal(11, environmentVariables.Count);

            // verify base request properties
            Assert.Equal(request.GetDisplayUrl(), environmentVariables["REQ_ORIGINAL_URL"]);
            Assert.Equal(request.Method.ToString(), environmentVariables["REQ_METHOD"]);
            Assert.Equal(request.QueryString.ToString(), environmentVariables["REQ_QUERY"]);

            // verify query parameters
            Assert.Equal("1", environmentVariables["REQ_QUERY_A"]);
            Assert.Equal("3", environmentVariables["REQ_QUERY_B"]);
            Assert.Equal("4", environmentVariables["REQ_QUERY_C"]);

            // verify headers
            Assert.Equal("a", environmentVariables["REQ_HEADERS_TEST-A"]);
            Assert.Equal("b", environmentVariables["REQ_HEADERS_TEST-B"]);
            Assert.Equal("test.com", environmentVariables["REQ_HEADERS_HOST"]);

            // verify route parameters
            Assert.Equal("123", environmentVariables["REQ_PARAMS_A"]);
            Assert.Equal("456", environmentVariables["REQ_PARAMS_B"]);
        }
    }
}
