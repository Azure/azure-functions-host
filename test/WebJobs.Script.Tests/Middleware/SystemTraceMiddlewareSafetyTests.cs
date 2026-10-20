// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware;

public sealed class SystemTraceMiddlewareSafetyTests
{
    /// <summary>
    /// Checks the new encoder against malformed strings in each formatted string field.
    /// </summary>
    [Theory]
    [InlineData(0, 0xD800)]
    [InlineData(1, 0xD800)]
    [InlineData(2, 0xD800)]
    [InlineData(0, 0xDC00)]
    [InlineData(1, 0xDC00)]
    [InlineData(2, 0xDC00)]
    public void Formatter_InvalidUtf16_DoesNotThrow(int field, int character)
    {
        string value = new((char)character, 1);
        var state = new ExecutedHttpRequestLogState(
            field == 0 ? value : "request",
            field == 1 ? value : string.Empty,
            200,
            1,
            field == 2 ? value : "api/test");

        string message = state.ToString();
        using var parsed = JsonDocument.Parse(message[(message.IndexOf(':') + 1)..]);
        string propertyName = field switch
        {
            0 => "requestId",
            1 => "identities",
            _ => "route",
        };
        Assert.Equal("\uFFFD", parsed.RootElement.GetProperty(propertyName).GetString());
        Assert.Equal(value, state[field == 2 ? 4 : field].Value);
    }

    /// <summary>
    /// Exercises a valid matching route whose configured regex contains a malformed code unit.
    /// </summary>
    [Fact]
    public async Task Middleware_InvalidUtf16InMatchedRegex_DoesNotFailRequest()
    {
        string template = "items/{id:regex(^[0-9" + '\uD800' + "]+$)}";
        var loggerProvider = new TestLoggerProvider();
        var handler = new Mock<IWebJobsRouteHandler>(MockBehavior.Strict);
        bool invoked = false;
        handler.Setup(value => value.InvokeAsync(It.IsAny<HttpContext>(), "Test"))
            .Returns<HttpContext, string>((context, _) =>
            {
                invoked = true;
                context.Response.StatusCode = 200;

                return Task.CompletedTask;
            });

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(loggerProvider));
        services.AddSingleton(handler.Object);
        services.AddHttpBindingRouting();
        services.AddMvc(options => options.EnableEndpointRouting = false);
        using var provider = services.BuildServiceProvider();
        var router = provider.GetRequiredService<IWebJobsRouter>();
        var routes = router.CreateBuilder(handler.Object, "api");
        routes.MapFunctionRoute("Test", template, "Test");
        router.AddFunctionRoutes(routes.Build(), null);
        var app = new ApplicationBuilder(provider);
        app.UseMiddleware<SystemTraceMiddleware>();
        app.UseMiddleware<ExceptionMiddleware>();
        app.UseHttpBindingRouting(null);
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = "GET";
        context.Request.Path = "/api/items/123";

        await app.Build()(context);

        Assert.True(invoked);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.DoesNotContain(loggerProvider.GetAllLogMessages(), log =>
            string.Equals(log.Category, typeof(ExceptionMiddleware).FullName, StringComparison.Ordinal));
        var completed = Assert.Single(loggerProvider.GetAllLogMessages().Where(log => log.EventId.Id == 528));
        using var parsed = JsonDocument.Parse(completed.FormattedMessage[(completed.FormattedMessage.IndexOf(':') + 1)..]);
        Assert.Equal("api/" + template.Replace("\uD800", "\uFFFD", StringComparison.Ordinal), parsed.RootElement.GetProperty("route").GetString());
    }

    /// <summary>
    /// Checks nulls, culture-independent values, controls and large valid text.
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void Formatter_ValidAndNullStrings_ParseAcrossCultures(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            foreach (string value in new[] { null, string.Empty, "\"\\\r\n\t\0", "\U0001F600", new string('x', 64 * 1024) })
            {
                var state = new ExecutedHttpRequestLogState(value, value, 503, long.MaxValue, value);
                string message = state.ToString();
                using var parsed = JsonDocument.Parse(message[(message.IndexOf(':') + 1)..]);
                Assert.Equal(value ?? "(null)", parsed.RootElement.GetProperty("route").GetString());
                Assert.Equal("503", parsed.RootElement.GetProperty("status").GetString());
                Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture), parsed.RootElement.GetProperty("duration").GetString());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// Verifies shared filter instances do not mix request templates.
    /// </summary>
    [Fact]
    public async Task FilterAndFormatter_ParallelRequests_StayIsolated()
    {
        var filter = new HttpRouteTemplateFilter();
        await Task.WhenAll(Enumerable.Range(0, 512).Select(async index =>
        {
            await Task.Yield();
            string template = $"admin/test{index}/{{id}}";
            var context = new DefaultHttpContext();
            var action = new ActionContext(context, new RouteData(), new ActionDescriptor
            {
                AttributeRouteInfo = new AttributeRouteInfo { Template = template }
            });
            filter.OnAuthorization(new AuthorizationFilterContext(action, new List<IFilterMetadata>()));
            Assert.Equal(template, SystemTraceMiddleware.GetRouteTemplate(context));
            string message = new ExecutedHttpRequestLogState($"request{index}", string.Empty, 200, index, template).ToString();
            using var parsed = JsonDocument.Parse(message[(message.IndexOf(':') + 1)..]);
            Assert.Equal(template, parsed.RootElement.GetProperty("route").GetString());
        }));
    }
}
