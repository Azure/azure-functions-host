// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware;

public class FunctionInvocationMiddlewareTests
{
    [Fact]
    public async Task Invoke_HeadNotAllowedFeature_Returns405WithAllowHeader()
    {
        using var server = new TestServer(new WebHostBuilder()
            .Configure(app =>
            {
                app.Use((ctx, next) =>
                {
                    ctx.Features.Set<IHeadNotAllowedFeature>(new HeadNotAllowedFeature("GET, POST"));
                    return next(ctx);
                });
                app.UseMiddleware<FunctionInvocationMiddleware>();
            }));

        var client = server.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Head, "/");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        string allow = string.Join(", ", response.Content.Headers.GetValues("Allow"));
        Assert.Equal("GET, POST", allow);
    }

    [Fact]
    public async Task Invoke_HeadNotAllowedFeature_GetRequest_Returns405()
    {
        // 405 fires regardless of request method when the feature is set —
        // it's the routing that determines this path, not the method.
        using var server = new TestServer(new WebHostBuilder()
            .Configure(app =>
            {
                app.Use((ctx, next) =>
                {
                    ctx.Features.Set<IHeadNotAllowedFeature>(new HeadNotAllowedFeature("POST"));
                    return next(ctx);
                });
                app.UseMiddleware<FunctionInvocationMiddleware>();
            }));

        var client = server.CreateClient();
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("POST", response.Content.Headers.GetValues("Allow").Single());
    }

    [Fact]
    public async Task Invoke_HeadRequest_OnGetFunction_SuppressesResponseBody()
    {
        var mockExecution = CreateMockFunctionExecution("TestFunction", "get");

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services => { services.AddLogging(); services.AddMvcCore(); })
            .Configure(app =>
            {
                app.Use((ctx, next) =>
                {
                    ctx.Features.Set<IFunctionExecutionFeature>(mockExecution.Object);
                    ctx.Features.Set<IRoutingFeature>(new RoutingFeature
                    {
                        RouteData = new RouteData()
                    });
                    return next(ctx);
                });
                app.UseMiddleware<FunctionInvocationMiddleware>();
            }));

        var client = server.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Head, "/");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Empty(body);
    }

    [Fact]
    public async Task Invoke_GetRequest_OnGetFunction_PreservesResponseBody()
    {
        var mockExecution = CreateMockFunctionExecution("TestFunction", "get");

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services => { services.AddLogging(); services.AddMvcCore(); })
            .Configure(app =>
            {
                app.Use((ctx, next) =>
                {
                    ctx.Features.Set<IFunctionExecutionFeature>(mockExecution.Object);
                    ctx.Features.Set<IRoutingFeature>(new RoutingFeature
                    {
                        RouteData = new RouteData()
                    });
                    return next(ctx);
                });
                app.UseMiddleware<FunctionInvocationMiddleware>();
            }));

        var client = server.CreateClient();
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal("response body content", body);
    }

    private static Mock<IFunctionExecutionFeature> CreateMockFunctionExecution(string functionName, params string[] methods)
    {
        var mockDescriptor = new Mock<FunctionDescriptor>();
        mockDescriptor.SetupGet(d => d.HttpTriggerAttribute)
            .Returns(new HttpTriggerAttribute(AuthorizationLevel.Anonymous, methods));
        mockDescriptor.Object.Name = functionName;
        mockDescriptor.Object.Metadata = new FunctionMetadata { Name = functionName };

        // LogCategory is a non-virtual get-only property set only by the parameterized
        // constructor. Use reflection to set the compiler-generated backing field.
        typeof(FunctionDescriptor)
            .GetField("<LogCategory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(mockDescriptor.Object, $"Function.{functionName}");

        var mockExecution = new Mock<IFunctionExecutionFeature>();
        mockExecution.SetupGet(f => f.CanExecute).Returns(true);
        mockExecution.SetupGet(f => f.Descriptor).Returns(mockDescriptor.Object);
        mockExecution
            .Setup(f => f.ExecuteAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .Callback<HttpRequest, CancellationToken>((req, _) =>
            {
                req.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey] =
                    new ContentResult { Content = "response body content", StatusCode = 200 };
            })
            .Returns(Task.CompletedTask);

        return mockExecution;
    }
}
