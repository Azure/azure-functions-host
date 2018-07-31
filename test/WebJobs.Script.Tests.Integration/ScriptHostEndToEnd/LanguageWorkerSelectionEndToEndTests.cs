// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ScriptHostEndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(LanguageWorkerSelectionEndToEndTests))]
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
            TestFixture fixture = null;

            try
            {
                string functionName = "HttpTrigger";
                fixture = new TestFixture(new Collection<string> { functionName }, functionsWorkerLanguage);
                await fixture.InitializeAsync();

                string url = $"http://localhost/api/{functionName}?name=test";
                var request = HttpTestHelpers.CreateHttpRequest("GET", url);

                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "request", request }
                };
                if (string.Equals(LanguageWorkerConstants.NodeLanguageWorkerName, functionsWorkerLanguage, System.StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(functionsWorkerLanguage))
                {
                    await fixture.Host.CallAsync(functionName, arguments);
                    var result = (IActionResult)request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];
                    Assert.IsType<RawScriptResult>(result);
                    var objResult = result as RawScriptResult;
                    Assert.Equal(200, objResult.StatusCode);
                }
                else
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Host.CallAsync(functionName, arguments));
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, string.Empty);
                await fixture?.DisposeAsync();
            }
        }

        private class TestFixture : NodeScriptHostTests.TestFixture
        {
            public TestFixture(ICollection<string> functions, string functionsWorkerLanguage)
                : base(functions, functionsWorkerLanguage)
            {
            }

            protected override Task CreateTestStorageEntities()
            {
                // No need for this, so let's save some time.
                return Task.CompletedTask;
            }
        }
    }
}
