// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(DirectLoadEndToEndTests))]
    public class DirectLoadEndToEndTests : EndToEndTestsBase<DirectLoadEndToEndTests.TestFixture>
    {
        public DirectLoadEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task Invoke_Succeeds()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://functions/function1?name=Mathew");
            var arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.SetConfiguration(Fixture.RequestConfiguration);

            await Fixture.Host.CallAsync("Function1", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];

            Assert.Equal("Hello Mathew", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Invoke_NoFunctionNameAttribute_Succeeds()
        {
            var arguments = new Dictionary<string, object>()
            {
                { "message", "test" }
            };
            await Fixture.Host.CallAsync("QueueTrigger", arguments);

            var trace = Fixture.TraceWriter.GetTraces().SingleOrDefault(p => p.Message == "C# Queue trigger function processed message 'test'.");
            Assert.NotNull(trace);
        }

        [Fact]
        public async Task Invoke_InvalidFunction_Fails()
        {
            var arguments = new Dictionary<string, object>()
            {
                { "message", "test" }
            };
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await Fixture.Host.CallAsync("InvalidFunction", arguments);
            });

            Assert.Equal("Unable to resolve function name 'InvalidFunction'.", ex.Message);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\DirectLoad\", "dotnet")
            {
            }

            public override void Dispose()
            {
                base.Dispose();
            }
        }
    }
}
