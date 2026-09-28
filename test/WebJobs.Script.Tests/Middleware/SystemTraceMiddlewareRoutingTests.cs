// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Handlers;

/// <summary>
/// Verifies request logging against the MVC and HTTP function routing pipelines.
/// </summary>
public sealed class SystemTraceMiddlewareRoutingTests
{
    private readonly TestLoggerProvider _loggerProvider = new();

    /// <summary>
    /// Verifies that MVC templates, rather than request parameter values, are logged.
    /// </summary>
    [Theory]
    [InlineData("/admin/host/status", "admin/host/status")]
    [InlineData("/admin/host/scale/status", "admin/host/scale/status")]
    [InlineData("/admin/functions/someone@contoso.com", "admin/functions/{name}")]
    public async Task SendAsync_WritesMatchedMvcRouteTemplate(string path, string template)
    {
        using IHost host = await CreateHostAsync();
        using var client = host.GetTestClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var log = GetCompletionLog();
        using var details = ParseDetails(log);
        Assert.Equal(template, details.RootElement.GetProperty("route").GetString());
        Assert.DoesNotContain("someone@contoso.com", log.FormattedMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that authorization failures retain the route selected by MVC.
    /// </summary>
    [Fact]
    public async Task SendAsync_WritesMvcRouteTemplate_WhenAuthorizationShortCircuits()
    {
        using IHost host = await CreateHostAsync(rejectAuthorization: true);
        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/admin/host/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var details = ParseDetails(GetCompletionLog());
        Assert.Equal("admin/host/status", details.RootElement.GetProperty("route").GetString());
        Assert.Equal("401", details.RootElement.GetProperty("status").GetString());
    }

    /// <summary>
    /// Verifies that unmatched requests never fall back to their raw paths.
    /// </summary>
    [Fact]
    public async Task SendAsync_WritesEmptyRoute_WhenNoRouteMatches()
    {
        using IHost host = await CreateHostAsync();
        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/unmatched/someone@contoso.com");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var log = GetCompletionLog();
        using var details = ParseDetails(log);
        Assert.Equal(string.Empty, details.RootElement.GetProperty("route").GetString());
        Assert.DoesNotContain("someone@contoso.com", log.FormattedMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that real function routes produce valid JSON without changing the structured route property.
    /// </summary>
    [Theory]
    [InlineData("orders/{id}")]
    [InlineData(@"orders/{id:regex(^\d+$)}")]
    [InlineData(@"orders/{id:regex(^\b[0-9]+\b$)}")]
    [InlineData("orders/{id:regex(^[0-9\"]+$)}")]
    public async Task SendAsync_WritesValidJson_ForFunctionRoute(string routeTemplate)
    {
        using IHost host = await CreateHostAsync(routeTemplate);
        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/orders/123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var log = GetCompletionLog();
        using var details = ParseDetails(log);
        Assert.Equal("200", details.RootElement.GetProperty("status").GetString());
        Assert.Equal($"api/{routeTemplate}", details.RootElement.GetProperty("route").GetString());
        var structuredRoute = log.State.Single(property => string.Equals(property.Key, "route", StringComparison.Ordinal)).Value;
        Assert.Equal($"api/{routeTemplate}", structuredRoute);
    }

    private Task<IHost> CreateHostAsync(string functionRoute = null, bool rejectAuthorization = false)
    {
        const string functionName = "TestFunction";
        var handler = new Mock<IWebJobsRouteHandler>(MockBehavior.Strict);
        handler.Setup(value => value.InvokeAsync(It.IsAny<HttpContext>(), functionName))
            .Returns(Task.CompletedTask);
        var authorizationFilter = new Mock<IAuthorizationFilter>();
        authorizationFilter.Setup(filter => filter.OnAuthorization(It.IsAny<AuthorizationFilterContext>()))
            .Callback<AuthorizationFilterContext>(context => context.Result = new UnauthorizedResult());

        return new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.AddProvider(_loggerProvider));
                    services.AddSingleton(handler.Object);
                    services.AddHttpBindingRouting();
                    services.AddMvc(options =>
                    {
                        options.EnableEndpointRouting = false;
                        if (rejectAuthorization)
                        {
                            options.Filters.Add(authorizationFilter.Object);
                        }

                        options.Filters.Add(new HttpRouteTemplateFilter());
                    })
                    .ConfigureApplicationPartManager(parts =>
                    {
                        parts.ApplicationParts.Clear();
                        parts.FeatureProviders.Add(new TestControllerFeatureProvider());
                    });
                })
                .Configure(app =>
                {
                    if (functionRoute is not null)
                    {
                        var router = app.ApplicationServices.GetRequiredService<IWebJobsRouter>();
                        var routes = router.CreateBuilder(handler.Object, routePrefix: "api");
                        routes.MapFunctionRoute(functionName, functionRoute, functionName);
                        router.AddFunctionRoutes(routes.Build(), null);
                    }

                    app.UseMiddleware<SystemTraceMiddleware>();
                    app.UseMvc();
                    app.UseHttpBindingRouting(null);
                }))
            .StartAsync();
    }

    private LogMessage GetCompletionLog()
    {
        return Assert.Single(_loggerProvider.GetAllLogMessages().Where(message =>
            string.Equals(message.Category, typeof(SystemTraceMiddleware).FullName, StringComparison.Ordinal)
            && message.EventId.Id == 528));
    }

    private static JsonDocument ParseDetails(LogMessage log)
    {
        return JsonDocument.Parse(log.FormattedMessage[(log.FormattedMessage.IndexOf(':') + 1)..]);
    }

    private sealed class TestControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Add(typeof(TestController).GetTypeInfo());
        }
    }

    /// <summary>
    /// Provides representative attribute-routed admin endpoints.
    /// </summary>
    public sealed class TestController : ControllerBase
    {
        /// <summary>
        /// Returns a successful status for the selected route.
        /// </summary>
        [HttpGet("admin/host/status")]
        [HttpGet("admin/host/scale/status")]
        [HttpGet("admin/functions/{name}")]
        public IActionResult GetStatus()
        {
            return Ok();
        }
    }
}
