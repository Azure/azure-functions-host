// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ScriptHostEndToEnd
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(LanguageWorkerSelectionEndToEndTests))]
    public class LanguageWorkerSelectionEndToEndTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("node")]
        [InlineData("Node")]
        [InlineData("dotNet")]
        public async Task HttpTrigger_Get(string functionsWorkerLanguage)
        {
            try
            {
                string functionName = "HttpTrigger";
                var fixture = new NodeScriptHostTests.TestFixture(new Collection<string> { functionName }, functionsWorkerLanguage);
                string url = $"http://localhost/api/{functionName}?name=test";
                var request = HttpTestHelpers.CreateHttpRequest("GET", url);

                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "request", request }
                };
                if (string.Equals(ScriptConstants.NodeLanguageWorkerName, functionsWorkerLanguage, System.StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(functionsWorkerLanguage))
                {
                    await fixture.Host.CallAsync(functionName, arguments);
                    var result = (IActionResult)request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];
                    Assert.IsType<ScriptObjectResult>(result);
                    var objResult = result as ScriptObjectResult;
                    Assert.Equal(200, objResult.StatusCode);
                }
                else
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Host.CallAsync(functionName, arguments));
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(ScriptConstants.FunctionWorkerRuntimeSettingName, string.Empty);
            }
        }
    }
}
