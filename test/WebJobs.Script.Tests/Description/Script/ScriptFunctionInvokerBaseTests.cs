// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.Script
{
    public class ScriptFunctionInvokerBaseTests
    {
        [Fact]
        public void InitializeHttpRequestEnvironmentVariables_SetsExpectedVariables()
        {
            var environmentVariables = new Dictionary<string, string>();
            var request = new HttpRequestMessage(HttpMethod.Get, "http://test.com/test?a=1&b=2&b=3&c=4");
            request.Headers.Add("TEST-A", "a");
            request.Headers.Add("TEST-B", "b");

            var routeData = new Dictionary<string, object>
            {
                { "a", 123 },
                { "b", 456 }
            };
            request.Properties.Add(ScriptConstants.AzureFunctionsHttpRouteDataKey, routeData);

            ScriptFunctionInvokerBase.InitializeHttpRequestEnvironmentVariables(environmentVariables, request);
            Assert.Equal(10, environmentVariables.Count);

            // verify base request properties
            Assert.Equal(request.RequestUri.ToString(), environmentVariables["REQ_ORIGINAL_URL"]);
            Assert.Equal(request.Method.ToString(), environmentVariables["REQ_METHOD"]);
            Assert.Equal(request.RequestUri.Query.ToString(), environmentVariables["REQ_QUERY"]);

            // verify query parameters
            Assert.Equal("1", environmentVariables["REQ_QUERY_A"]);
            Assert.Equal("3", environmentVariables["REQ_QUERY_B"]);
            Assert.Equal("4", environmentVariables["REQ_QUERY_C"]);

            // verify headers
            Assert.Equal("a", environmentVariables["REQ_HEADERS_TEST-A"]);
            Assert.Equal("b", environmentVariables["REQ_HEADERS_TEST-B"]);

            // verify route parameters
            Assert.Equal("123", environmentVariables["REQ_PARAMS_A"]);
            Assert.Equal("456", environmentVariables["REQ_PARAMS_B"]);
        }
    }
}
