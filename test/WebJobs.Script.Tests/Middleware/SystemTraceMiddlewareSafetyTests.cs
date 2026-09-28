// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware;

/// <summary>
/// Verifies that request tracing cannot turn diagnostic failures into application failures.
/// </summary>
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

    /// <summary>
    /// Exercises cancellation before and after response headers start.
    /// </summary>
    [Theory]
    [InlineData(false, 499)]
    [InlineData(true, 200)]
    public async Task Middleware_CancelledRequests_LogWithoutThrowing(bool responseStarted, int expectedStatus)
    {
        var context = new DefaultHttpContext();
        var responseFeature = new Mock<IHttpResponseFeature>();
        responseFeature.SetupProperty(feature => feature.StatusCode, 200);
        responseFeature.SetupGet(feature => feature.HasStarted).Returns(responseStarted);
        context.Features.Set(responseFeature.Object);
        context.Features.Set(new HttpRouteTemplateFeature("api/test/{id}"));
        context.RequestAborted = new CancellationToken(canceled: true);
        var logger = new TestLogger<SystemTraceMiddleware>();
        var cancellation = new HandleCancellationMiddleware(
            _ => throw new OperationCanceledException(context.RequestAborted),
            NullLogger<HandleCancellationMiddleware>.Instance);
        var middleware = new SystemTraceMiddleware(cancellation.Invoke, logger);

        await middleware.Invoke(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        var completed = Assert.Single(logger.GetLogMessages().Where(log => log.EventId.Id == 528));
        using var parsed = JsonDocument.Parse(completed.FormattedMessage[(completed.FormattedMessage.IndexOf(':') + 1)..]);
        Assert.Equal("api/test/{id}", parsed.RootElement.GetProperty("route").GetString());
    }

    /// <summary>
    /// Verifies provider failures cannot prevent execution or alter a completed response.
    /// </summary>
    [Theory]
    [InlineData(527, false)]
    [InlineData(527, true)]
    [InlineData(528, false)]
    [InlineData(528, true)]
    public async Task Middleware_ThrowingLogger_PreservesRequest(int failingEventId, bool responseStarted)
    {
        using var listener = new TraceEventListener();
        var failure = new IOException("Sensitive diagnostic value: someone@contoso.com");
        var logger = CreateThrowingLogger(failingEventId, failure);
        var context = new DefaultHttpContext();
        bool started = false;
        var responseFeature = new Mock<IHttpResponseFeature>();
        responseFeature.SetupProperty(feature => feature.StatusCode, 202);
        responseFeature.SetupGet(feature => feature.HasStarted).Returns(() => started);
        context.Features.Set(responseFeature.Object);
        bool invoked = false;
        var middleware = new SystemTraceMiddleware(_ =>
        {
            invoked = true;
            started = responseStarted;

            return Task.CompletedTask;
        }, logger.Object);

        await middleware.Invoke(context);

        Assert.True(invoked);
        Assert.Equal(202, context.Response.StatusCode);
        string operation = failingEventId == 527
            ? nameof(ScriptHostServiceLoggerExtension.ExecutingHttpRequest)
            : nameof(ScriptHostServiceLoggerExtension.ExecutedHttpRequest);
        AssertFailureReported(listener, operation, typeof(IOException));
    }

    /// <summary>
    /// Verifies disabled tracing does not enumerate identities or inspect routing features.
    /// </summary>
    [Fact]
    public async Task Middleware_DisabledLogging_DoesNotReadDiagnosticMetadata()
    {
        using var listener = new TraceEventListener();
        var principal = new Mock<ClaimsPrincipal>();
        principal.SetupGet(value => value.Identities).Throws(new IOException("Identities unavailable"));
        var routing = new Mock<IRoutingFeature>();
        routing.SetupGet(value => value.RouteData).Throws(new IOException("Route data unavailable"));
        var context = new DefaultHttpContext { User = principal.Object };
        context.Features.Set(routing.Object);
        var middleware = new SystemTraceMiddleware(_ => Task.CompletedTask, NullLogger<SystemTraceMiddleware>.Instance);

        await middleware.Invoke(context);

        Assert.Equal(200, context.Response.StatusCode);
        principal.VerifyGet(value => value.Identities, Times.Never);
        routing.VerifyGet(value => value.RouteData, Times.Never);
        Assert.Empty(listener.Events);
    }

    /// <summary>
    /// Verifies identity buffers are cleared after a failed extraction.
    /// </summary>
    [Fact]
    public async Task Middleware_IdentityFailure_DoesNotLeakIntoNextRequest()
    {
        var identity = new Mock<ClaimsIdentity>();
        identity.SetupGet(value => value.IsAuthenticated).Returns(true);
        identity.SetupGet(value => value.AuthenticationType).Returns("FailedIdentity");
        identity.SetupGet(value => value.Claims).Throws(new IOException("Claims unavailable"));
        var logger = new TestLogger<SystemTraceMiddleware>();
        var middleware = new SystemTraceMiddleware(_ => Task.CompletedTask, logger);

        await middleware.Invoke(new DefaultHttpContext { User = new ClaimsPrincipal(identity.Object) });
        await middleware.Invoke(new DefaultHttpContext());

        var completed = Assert.Single(logger.GetLogMessages().Where(log => log.EventId.Id == 528));
        using var details = JsonDocument.Parse(completed.FormattedMessage[(completed.FormattedMessage.IndexOf(':') + 1)..]);
        Assert.Equal(string.Empty, details.RootElement.GetProperty("identities").GetString());
    }

    /// <summary>
    /// Verifies mixed identities retain their ordering, separators and first matching auth-level claim.
    /// </summary>
    [Fact]
    public async Task Middleware_MixedIdentities_PreservesFormatting()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
            [
                new ClaimsIdentity(),
                new ClaimsIdentity([], "NoLevel"),
                new ClaimsIdentity(
                [
                    new Claim(SecurityConstants.AuthLevelClaimType, "Function"),
                    new Claim(SecurityConstants.AuthLevelClaimType, "Admin"),
                ], "WithLevel"),
            ])
        };
        var logger = new TestLogger<SystemTraceMiddleware>();
        var middleware = new SystemTraceMiddleware(_ => Task.CompletedTask, logger);

        await middleware.Invoke(context);

        var completed = Assert.Single(logger.GetLogMessages().Where(log => log.EventId.Id == 528));
        using var details = JsonDocument.Parse(completed.FormattedMessage[(completed.FormattedMessage.IndexOf(':') + 1)..]);
        Assert.Equal("(NoLevel, WithLevel:Function)", details.RootElement.GetProperty("identities").GetString());
    }

    /// <summary>
    /// Verifies that IsEnabled failures are contained as well as Log failures.
    /// </summary>
    [Fact]
    public async Task Middleware_ThrowingIsEnabled_PreservesRequest()
    {
        using var listener = new TraceEventListener();
        var logger = new Mock<ILogger<SystemTraceMiddleware>>();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Throws(new IOException("Provider unavailable"));
        var context = new DefaultHttpContext();
        var middleware = new SystemTraceMiddleware(httpContext =>
        {
            httpContext.Response.StatusCode = 204;

            return Task.CompletedTask;
        }, logger.Object);

        await middleware.Invoke(context);

        Assert.Equal(204, context.Response.StatusCode);
        Assert.Equal(2, listener.Events.Count);
    }

    /// <summary>
    /// Verifies failures while extracting identities do not change the response.
    /// </summary>
    [Fact]
    public async Task Middleware_ThrowingIdentity_PreservesRequest()
    {
        using var listener = new TraceEventListener();
        var identity = new Mock<ClaimsIdentity>();
        identity.SetupGet(value => value.IsAuthenticated).Returns(true);
        identity.SetupGet(value => value.Claims).Throws(new IOException("Claims unavailable"));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity.Object) };
        var logger = new TestLogger<SystemTraceMiddleware>();
        var middleware = new SystemTraceMiddleware(_ => Task.CompletedTask, logger);

        await middleware.Invoke(context);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(527, Assert.Single(logger.GetLogMessages()).EventId.Id);
        AssertFailureReported(listener, nameof(ScriptHostServiceLoggerExtension.ExecutedHttpRequest), typeof(IOException));
    }

    /// <summary>
    /// Verifies failures while extracting route data do not change the response.
    /// </summary>
    [Fact]
    public async Task Middleware_ThrowingRoutingFeature_PreservesRequest()
    {
        using var listener = new TraceEventListener();
        var routing = new Mock<IRoutingFeature>();
        routing.SetupGet(value => value.RouteData).Throws(new IOException("Route data unavailable"));
        var context = new DefaultHttpContext();
        context.Features.Set(routing.Object);
        var logger = new TestLogger<SystemTraceMiddleware>();
        var middleware = new SystemTraceMiddleware(_ => Task.CompletedTask, logger);

        await middleware.Invoke(context);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(527, Assert.Single(logger.GetLogMessages()).EventId.Id);
        AssertFailureReported(listener, nameof(ScriptHostServiceLoggerExtension.ExecutedHttpRequest), typeof(IOException));
    }

    /// <summary>
    /// Verifies route-feature failures cannot become MVC authorization failures.
    /// </summary>
    [Fact]
    public void Filter_ThrowingFeatureCollection_DoesNotRejectRequest()
    {
        using var listener = new TraceEventListener();
        var features = new Mock<IFeatureCollection>();
        features.Setup(value => value.Set(It.IsAny<HttpRouteTemplateFeature>())).Throws(new InvalidOperationException("Read-only features"));
        var httpContext = new Mock<HttpContext>();
        httpContext.SetupGet(value => value.Features).Returns(features.Object);
        var action = new ActionContext(httpContext.Object, new RouteData(), new ActionDescriptor
        {
            AttributeRouteInfo = new AttributeRouteInfo { Template = "admin/test" }
        });
        var context = new AuthorizationFilterContext(action, new List<IFilterMetadata>());

        new HttpRouteTemplateFilter().OnAuthorization(context);

        Assert.Null(context.Result);
        AssertFailureReported(listener, nameof(HttpRouteTemplateFilter), typeof(InvalidOperationException));
    }

    /// <summary>
    /// Verifies the diagnostic fallback does not rethrow failing EventSource listeners.
    /// </summary>
    [Fact]
    public async Task Middleware_ThrowingDiagnosticListener_DoesNotFailRequest()
    {
        using var listener = new TraceEventListener(throwOnEvent: true);
        var logger = CreateThrowingLogger(528, new IOException("Provider unavailable"));
        var context = new DefaultHttpContext();
        var middleware = new SystemTraceMiddleware(_ => Task.CompletedTask, logger.Object);

        await middleware.Invoke(context);

        Assert.Equal(200, context.Response.StatusCode);
        AssertFailureReported(listener, nameof(ScriptHostServiceLoggerExtension.ExecutedHttpRequest), typeof(IOException));
    }

    /// <summary>
    /// Verifies fatal errors are not hidden by diagnostic isolation.
    /// </summary>
    [Fact]
    public async Task Middleware_AggregateWithFatalError_IsNotSwallowed()
    {
        var failure = new AggregateException(new IOException(), new OutOfMemoryException());
        var logger = CreateThrowingLogger(528, failure);
        var middleware = new SystemTraceMiddleware(_ => Task.CompletedTask, logger.Object);

        Assert.False(HttpRequestTraceDiagnostics.IsRecoverable(failure));
        Assert.Same(failure, await Assert.ThrowsAsync<AggregateException>(() => middleware.Invoke(new DefaultHttpContext())));
    }

    /// <summary>
    /// Verifies application exceptions still flow to the existing exception handling pipeline.
    /// </summary>
    [Fact]
    public async Task Middleware_ApplicationException_IsNotSwallowed()
    {
        var failure = new InvalidOperationException("Application failure");
        var middleware = new SystemTraceMiddleware(_ => throw failure, NullLogger<SystemTraceMiddleware>.Instance);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(new DefaultHttpContext())));
    }

    private static Mock<ILogger<SystemTraceMiddleware>> CreateThrowingLogger(int eventId, Exception exception)
    {
        var logger = new Mock<ILogger<SystemTraceMiddleware>>();
        logger.Setup(value => value.IsEnabled(LogLevel.Information)).Returns(true);
        logger.Setup(value => value.Log(
            LogLevel.Information,
            It.Is<EventId>(id => id.Id == eventId),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>())).Throws(exception);

        return logger;
    }

    private static void AssertFailureReported(TraceEventListener listener, string operation, Type exceptionType)
    {
        var entry = Assert.Single(listener.Events);
        Assert.Equal(operation, entry.Payload[0]);
        Assert.Equal(exceptionType.FullName, entry.Payload[1]);
        Assert.Equal(2, entry.Payload.Count);
    }

    private sealed class TraceEventListener : EventListener
    {
        public TraceEventListener(bool throwOnEvent = false)
        {
            EventWritten += (_, entry) =>
            {
                if (entry.EventId == 1)
                {
                    Events.Enqueue(entry);
                    if (throwOnEvent)
                    {
                        throw new IOException("Listener unavailable");
                    }
                }
            };
            EventSourceCreated += (_, entry) =>
            {
                if (string.Equals(entry.EventSource.Name, ScriptConstants.HostEventSourcePrefix + nameof(HttpRequestTraceDiagnostics), StringComparison.Ordinal))
                {
                    EnableEvents(entry.EventSource, EventLevel.Warning);
                }
            };
        }

        public ConcurrentQueue<EventWrittenEventArgs> Events { get; } = new();
    }
}
