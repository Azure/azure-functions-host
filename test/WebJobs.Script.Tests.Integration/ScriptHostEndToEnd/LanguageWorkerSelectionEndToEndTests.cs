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
    public class LanguageWorkerSelectionEndToEndTests : IClassFixture<LanguageWorkerSelectionEndToEndTests.TestFixture>
    {
        TestFixture Fixture;

        public LanguageWorkerSelectionEndToEndTests(TestFixture fixture)
        {
            Fixture = fixture;
        }

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
                await Fixture.InitializeAsync();

                string url = $"http://localhost/api/{functionName}?name=test";
                var request = HttpTestHelpers.CreateHttpRequest("GET", url);

                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "request", request }
                };
                if (string.Equals(LanguageWorkerConstants.NodeLanguageWorkerName, functionsWorkerLanguage, System.StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(functionsWorkerLanguage))
                {
                    await Fixture.Host.CallAsync(functionName, arguments);
                    var result = (IActionResult)request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];
                    Assert.IsType<RawScriptResult>(result);
                    var objResult = result as RawScriptResult;
                    Assert.Equal(200, objResult.StatusCode);
                }
                else
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(async () => await Fixture.Host.CallAsync(functionName, arguments));
                }
            }
            finally
            {
                await Fixture?.DisposeAsync();
            }
        }

        public class TestFixture : ScriptHostEndToEndTestFixture
        {
            static TestFixture()
            {
            }

            public TestFixture() : base(@"TestScripts\Node", "node", LanguageWorkerConstants.NodeLanguageWorkerName)
            {
            }

            protected override Task CreateTestStorageEntities()
            {
                // No need for this.
                return Task.CompletedTask;
            }
        }
    }
}
