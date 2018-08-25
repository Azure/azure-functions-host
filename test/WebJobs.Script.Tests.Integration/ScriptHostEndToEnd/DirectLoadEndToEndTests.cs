// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(DirectLoadEndToEndTests))]
    public class DirectLoadEndToEndTests : IClassFixture<DirectLoadEndToEndTests.TestFixture>
    {
        TestFixture Fixture;

        public DirectLoadEndToEndTests(TestFixture fixture)
        {
            Fixture = fixture;
        }

        [Fact]
        public async Task Invoke_Succeeds()
        {
            var context = new DefaultHttpContext();
            var request = new DefaultHttpRequest(context)
            {
                Method = "GET",
                Scheme = "http",
                Host = new HostString("functions.com", 80),
                Path = "/api/functions/function1",
                QueryString = new QueryString("?name=Mathew")
            };
            var arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };
            request.Headers.Add("Accept", new StringValues("text/plain"));

            await Fixture.Host.CallAsync("Function1", arguments);

            var response = (OkObjectResult)request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];

            Assert.Equal("Hello, Mathew!", (string)response.Value);

            var log = Fixture.LoggerProvider.GetAllLogMessages().SingleOrDefault(p => p.FormattedMessage == "C# HTTP trigger function processed a request.");
            Assert.NotNull(log);
            Assert.Equal(LogLevel.Information, log.Level);
            Assert.Equal("Function1", log.Scope[ScopeKeys.FunctionName]);
            Assert.Equal(LogCategories.CreateFunctionUserCategory("Function1"), log.Category);
        }

        [Fact]
        public async Task Invoke_ExceptionThrown_DetailsLogged()
        {
            var context = new DefaultHttpContext();
            var request = new DefaultHttpRequest(context)
            {
                Method = "GET",
                Scheme = "http",
                Host = new HostString("functions.com", 80),
                Path = "/api/functions/function1",
                QueryString = new QueryString("?action=throw")
            };
            var arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };

            var ex = await Assert.ThrowsAsync<FunctionInvocationException>(async () =>
            {
                await Fixture.Host.CallAsync("Function1", arguments);
            });

            var response = request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];

            var errorLogs = Fixture.LoggerProvider.GetAllLogMessages().Where(p => p.Level == LogLevel.Error).ToArray();
            Assert.Equal(2, errorLogs.Length);

            // first log is the result
            Assert.Equal(LogCategories.Results, errorLogs[0].Category);

            var error = errorLogs[1];
            var invocationException = (FunctionInvocationException)error.Exception;
            Assert.Equal("Exception while executing function: Function1", invocationException.Message);
            Assert.Equal("TestFunctions.Function1.Run", invocationException.MethodName);
            Assert.Equal("Function1", error.Scope[ScopeKeys.FunctionName]);
            Assert.Equal(LogCategories.CreateFunctionCategory("Function1"), error.Category);
        }

        public class TestFixture : ScriptHostEndToEndTestFixture
        {
            static TestFixture()
            {
            }

            public TestFixture() : base(@"TestScripts\DirectLoad\", "dotnet", LanguageWorkerConstants.DotNetLanguageWorkerName)
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
