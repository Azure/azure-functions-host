// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;

[Trait(TestTraits.Category, TestTraits.EndToEnd)]
[Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
public class HostInfrastructureEndToEndTests(HostInfrastructureEndToEndTests.TestFixture fixture)
    : IClassFixture<HostInfrastructureEndToEndTests.TestFixture>
{
    [Fact]
    public async Task VerifyHostHeader()
    {
        const string actualHost = "actual-host";
        const string path = "api/httptrigger-scenarios";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost/{path}");
        request.Headers.TryAddWithoutValidation("DISGUISED-HOST", actualHost);
        request.Headers.Add("X-Forwarded-Proto", new[] { "https", "http" });
        request.Content = new StringContent(
            new JObject { ["scenario"] = "appServiceFixupMiddleware" }.ToString(), Encoding.UTF8, "application/json");
        using var response = await fixture.Host.HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        Assert.Equal($"https://{actualHost}/{path}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VerifyResultRedirect()
    {
        using var response = await fixture.Host.HttpClient.GetAsync("api/httptrigger-redirect");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Theory]
    [InlineData("accept", HttpStatusCode.Accepted)]
    [InlineData("create", HttpStatusCode.Created)]
    [InlineData("acceptBadRoute", HttpStatusCode.InternalServerError)]
    [InlineData("createBadRoute", HttpStatusCode.InternalServerError)]
    public async Task VerifyResult_OtherFunctionRoute(string action, HttpStatusCode expectedStatus)
    {
        using var response = await fixture.Host.HttpClient.GetAsync($"api/httptrigger-routed?action={action}");
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public void MultipleTriggers_ShowsHelpfulMessage()
    {
        var logs = fixture.Host.StartupLogs.Where(p => p.Level == LogLevel.Error && p.FormattedMessage.Contains("MultipleTriggers"));
        Assert.Equal("The 'MultipleTriggers' function is in error: Multiple trigger bindings defined. A function can only have a single trigger binding.",
            Assert.Single(logs).FormattedMessage);
    }

    [Fact]
    public async Task ExecutionContext_IsPopulated()
    {
        const string functionName = "FunctionExecutionContext";
        using var response = await fixture.Host.HttpClient.GetAsync($"api/{functionName}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ExecutionContext context = await response.Content.ReadAsAsync<ExecutionContext>();
        Assert.NotNull(context);
        Assert.Equal(functionName, context.FunctionName);
        Assert.Equal(Path.Combine(fixture.Host.ScriptOptions.RootScriptPath, functionName), context.FunctionDirectory);
    }

    [Fact]
    public async Task HttpTrigger_Post_Dynamic()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/HttpTrigger-Dynamic");
        request.Headers.Accept.ParseAdd("text/plain");
        request.Content = new StringContent(
            new JObject { ["name"] = "Mathew Charles", ["location"] = "Seattle" }.ToString(), Encoding.UTF8, "application/json");
        using var response = await fixture.Host.HttpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Name: Mathew Charles, Location: Seattle", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HttpTrigger_Model_Binding()
    {
        var payload = JObject.FromObject(new
        {
            custom = new { customProperty = "value" },
            customEnumerable = new[] { new { customProperty = "value1" }, new { customProperty = "value2" } }
        });
        using var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
        using var response = await fixture.Host.HttpClient.PostAsync("api/HttpTrigger-Model", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(JObject.DeepEquals(payload, result), result.ToString());
    }

    [Fact]
    public async Task FunctionWithIndexingError_ReturnsError()
    {
        var status = await fixture.Host.GetFunctionStatusAsync("FunctionIndexingError");
        string error = Assert.Single(status.Errors);
        Assert.Contains("Error indexing method", error);
        Assert.Contains("setting_does_not_exist", error);
    }

    public class TestFixture() : EndToEndTestFixture(@"TestScripts\NoFunction", "host-infrastructure", null)
    {
        public override void ConfigureWebHost(IServiceCollection services)
        {
            var multipleTriggers = HostInfrastructureTestFunctions.Http("MultipleTriggers", nameof(HostInfrastructureTestFunctions.Redirect));
            multipleTriggers.Bindings.Add(BindingMetadata.Create(new JObject
            {
                ["type"] = "httpTrigger", ["direction"] = "in", ["name"] = "req2", ["authLevel"] = "anonymous"
            }));
            services.AddSingleton<IFunctionProvider>(provider =>
            {
                var context = HostInfrastructureTestFunctions.Http("FunctionExecutionContext", nameof(HostInfrastructureTestFunctions.GetExecutionContext));
                context.FunctionDirectory = Path.Combine(
                    provider.GetRequiredService<IOptions<ScriptApplicationHostOptions>>().Value.ScriptPath, context.Name);

                return new TestCodelessFunctionProvider(
                [
                    HostInfrastructureTestFunctions.Http("HttpTrigger-Scenarios", nameof(HostInfrastructureTestFunctions.HttpScenarios)),
                    HostInfrastructureTestFunctions.Http("HttpTrigger-Redirect", nameof(HostInfrastructureTestFunctions.Redirect)),
                    HostInfrastructureTestFunctions.Http("HttpTrigger-Routed", nameof(HostInfrastructureTestFunctions.Routed)),
                    HostInfrastructureTestFunctions.Http("HttpTrigger-Dynamic", nameof(HostInfrastructureTestFunctions.DynamicBody), parameterName: "input"),
                    HostInfrastructureTestFunctions.Http("HttpTrigger-Model", nameof(HostInfrastructureTestFunctions.ModelBody), parameterName: "payload"),
                    HostInfrastructureTestFunctions.Create("FunctionIndexingError", nameof(HostInfrastructureTestFunctions.CustomTrigger),
                        new JObject
                        {
                            ["type"] = "queueTrigger", ["direction"] = "in", ["name"] = "input",
                            ["queueName"] = "test-input-extension", ["connection"] = "setting_does_not_exist"
                        }),
                    context,
                    multipleTriggers
                ], null);
            });
        }
    }
}
