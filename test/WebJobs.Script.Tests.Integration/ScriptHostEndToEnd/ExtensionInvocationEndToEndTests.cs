// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class ExtensionInvocationEndToEndTests : IClassFixture<ExtensionInvocationEndToEndTests.TestFixture>
    {
        private readonly TestFixture _fixture;

        public ExtensionInvocationEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Invoke_Succeeds()
        {
            _fixture.Host.ClearLogMessages();
            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Method = "GET";
            request.Scheme = "http";
            request.Host = new HostString("functions.com", 80);
            request.Path = "/api/functions/function1";
            request.QueryString = new QueryString("?name=Mathew");

            var arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };
            request.Headers.Append("Accept", new StringValues("text/plain"));

            await _fixture.Host.JobHostServices.GetRequiredService<IScriptJobHost>().CallAsync("Function1", arguments);

            var response = (OkObjectResult)request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];

            Assert.Equal("Hello, Mathew!", (string)response.Value);

            var log = _fixture.Host.GetScriptHostLogMessages().SingleOrDefault(p => p.FormattedMessage == "Test extension HTTP trigger function processed a request.");
            Assert.NotNull(log);
            Assert.Equal(LogLevel.Information, log.Level);
            Assert.Equal("Function1", log.Scope[ScopeKeys.FunctionName]);
            Assert.Equal(LogCategories.CreateFunctionUserCategory("Function1"), log.Category);
        }

        [Fact]
        public async Task Invoke_ExceptionThrown_DetailsLogged()
        {
            _fixture.Host.ClearLogMessages();
            var context = new DefaultHttpContext();

            var request = context.Request;
            request.Method = "GET";
            request.Scheme = "http";
            request.Host = new HostString("functions.com", 80);
            request.Path = "/api/functions/function1";
            request.QueryString = new QueryString("?action=throw");
            
            var arguments = new Dictionary<string, object>()
            {
                { "req", request }
            };

            var ex = await Assert.ThrowsAsync<FunctionInvocationException>(async () =>
            {
                await _fixture.Host.JobHostServices.GetRequiredService<IScriptJobHost>().CallAsync("Function1", arguments);
            });

            var response = request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];

            var errorLogs = _fixture.Host.GetScriptHostLogMessages().Where(p => p.Level == LogLevel.Error).ToArray();
            Assert.Equal(2, errorLogs.Length);

            // first log is the result
            Assert.Equal(LogCategories.Results, errorLogs[1].Category);

            var error = errorLogs[0];
            var invocationException = (FunctionInvocationException)error.Exception;
            Assert.Equal("Exception while executing function: Functions.Function1", invocationException.Message);
            Assert.Equal("Host.Functions.Function1", invocationException.MethodName);
            Assert.Equal("Function1", error.Scope[ScopeKeys.FunctionName]);
            Assert.Equal(LogCategories.CreateFunctionCategory("Function1"), error.Category);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\NoFunction", "extension-invocation", null)
            {
            }

            public override void ConfigureWebHost(IServiceCollection services)
            {
                services.AddSingleton<IFunctionProvider>(new TestCodelessFunctionProvider(
                [
                    HostInfrastructureTestFunctions.Http("Function1", nameof(HostInfrastructureTestFunctions.Invoke))
                ], null));
            }

            protected override Task CreateTestStorageEntities()
            {
                // No need for this.
                return Task.CompletedTask;
            }
        }
    }
}
