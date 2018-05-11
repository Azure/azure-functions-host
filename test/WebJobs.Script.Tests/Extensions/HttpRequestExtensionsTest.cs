// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class HttpRequestExtensionsTest
    {
        [Fact]
        public void IsAntaresInternalRequest_ReturnsExpectedResult()
        {
            // not running under Azure
            var request = HttpTestHelpers.CreateHttpRequest("GET", "http://foobar");
            Assert.False(request.IsAntaresInternalRequest());

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
                Assert.False(request.IsAntaresInternalRequest());

                request = HttpTestHelpers.CreateHttpRequest("GET", "http://foobar");
                Assert.True(request.IsAntaresInternalRequest());
            }
        }
    }
}
