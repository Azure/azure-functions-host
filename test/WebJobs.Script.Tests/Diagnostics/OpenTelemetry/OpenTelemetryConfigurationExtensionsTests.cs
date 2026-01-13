// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.OpenTelemetry
{
    public class OpenTelemetryConfigurationExtensionsTests
    {
        private readonly string _loggingPath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "Logging");

        [Fact]
        public void ConfigureTelemetry_Should_UseNothingIfNoKeysOrEndpointsPresent()
        {
            IServiceCollection sc = default;
            var hostBuilder = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost()
                .ConfigureLogging((ctx, lb) => lb.ConfigureTelemetry(ctx))
                .ConfigureServices(s => sc = s);

            using IHost host = hostBuilder.Build();

            // Assert
            sc.Should().NotBeNullOrEmpty();
            HasOtelServices(sc).Should().BeFalse();

            host.Services.GetService<TelemetryClient>().Should().BeNull();
        }

        [Fact]
        public void ConfigureTelemetry_Should_UseApplicationInsightsByDefaultIfKeyPresent()
        {
            IServiceCollection sc = default;
            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "APPINSIGHTS_INSTRUMENTATIONKEY", "some_key" },
                        { "APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=some_other_key" },
                        { ConfigurationPath.Combine(_loggingPath, "ApplicationInsights", "SamplingSettings", "IsEnabled"), "false" },
                        { ConfigurationPath.Combine(_loggingPath, "ApplicationInsights", "SnapshotConfiguration", "IsEnabled"), "false" }
                    });
                })
                .ConfigureDefaultTestWebScriptHost()
                .ConfigureLogging((ctx, lb) => lb.ConfigureTelemetry(ctx))
                .ConfigureServices(s => sc = s);

            using IHost host = hostBuilder.Build();

            // Assert
            sc.Should().NotBeNullOrEmpty();
            HasOtelServices(sc).Should().BeFalse();

            host.Services.GetService<TelemetryClient>().Should().NotBeNull();
        }

        [Fact]
        public void ConfigureTelemetry_Should_UseApplicationInsightsWhenModeSetAndKeysPresent()
        {
            IServiceCollection sc = default;
            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "APPINSIGHTS_INSTRUMENTATIONKEY", "some_key" },
                        { "APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=some_key" },
                        { ConfigurationPath.Combine(_loggingPath, "ApplicationInsights", "SamplingSettings", "IsEnabled"), "false" },
                        { ConfigurationPath.Combine(_loggingPath, "ApplicationInsights", "SnapshotConfiguration", "IsEnabled"), "false" },
                        { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "telemetryMode"), TelemetryMode.ApplicationInsights.ToString() },
                    });
                })
                .ConfigureDefaultTestWebScriptHost()
                .ConfigureLogging((ctx, lb) => lb.ConfigureTelemetry(ctx))
                .ConfigureServices(s => sc = s);

            using IHost host = hostBuilder.Build();

            // Assert
            sc.Should().NotBeNullOrEmpty();
            HasOtelServices(sc).Should().BeFalse();

            var telemetryClient = host.Services.GetService<TelemetryClient>();
            telemetryClient.Should().NotBeNull();

            var telmetryConfig = host.Services.GetService<TelemetryConfiguration>();
            telmetryConfig.Should().NotBeNull();
            telmetryConfig.ConnectionString.Should().Be("InstrumentationKey=some_key");
        }

        [Fact]
        public void ConfigureTelemetry_Should_UsesOpenTelemetryWhenModeSetAndAppInsightsKeysPresent()
        {
            IServiceCollection sc = default;
            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "APPINSIGHTS_INSTRUMENTATIONKEY", "some_key" },
                        { "APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=key" },
                        { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "telemetryMode"), TelemetryMode.OpenTelemetry.ToString() },
                    });
                })
                .ConfigureDefaultTestWebScriptHost()
                .ConfigureLogging((ctx, lb) => lb.ConfigureTelemetry(ctx))
                .ConfigureServices(s => sc = s);

            using IHost host = hostBuilder.Build();

            // Assert
            sc.Should().NotBeNullOrEmpty();
            HasOtelServices(sc).Should().BeTrue();

            host.Services.GetService<TelemetryClient>().Should().BeNull();
            host.Services.GetService<TelemetryConfiguration>().Should().BeNull();
        }

        [Fact]
        public void ConfigureTelemetry_Should_UsesOpenTelemetryWithOtlpExporterWhenEnvVarsSet()
        {
            IServiceCollection sc = default;
            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { EnvironmentSettingNames.AppInsightsInstrumentationKey, "some_key" },
                        { EnvironmentSettingNames.AppInsightsConnectionString, "InstrumentationKey=some_key" },
                        { "OTEL_EXPORTER_OTLP_ENDPOINT", "https://otlp.nr-data.net" },
                        { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "telemetryMode"), TelemetryMode.OpenTelemetry.ToString() },
                    });
                })
                .ConfigureDefaultTestWebScriptHost()
                .ConfigureServices(s => sc = s);

            using IHost host = hostBuilder.Build();

            // Assert
            sc.Should().NotBeNullOrEmpty();
            HasOtelServices(sc).Should().BeTrue();
            sc.Should().Contain(sd => sd.ServiceType.FullName == "OpenTelemetry.Trace.IConfigureTracerProviderBuilder");
            sc.Should().Contain(sd => sd.ServiceType.FullName == "OpenTelemetry.Logs.IConfigureLoggerProviderBuilder");
            sc.Should().Contain(sd => sd.ServiceType.FullName == "OpenTelemetry.Metrics.IConfigureMeterProviderBuilder");

            host.Services.GetService<TelemetryClient>().Should().BeNull();

            // Since no OTLP endpoint was given, this should all be null as well
            var otlpOptions = host.Services.GetService<OtlpExporterOptions>();
            otlpOptions?.Endpoint.Should().Be("https://otlp.nr-data.net");

            host.Services.GetService<IOptions<OpenTelemetryLoggerOptions>>()?.Value?.Should().NotBeNull();
            host.Services.GetService<IOptions<MetricReaderOptions>>()?.Value?.Should().NotBeNull();
            host.Services.GetService<IOptions<BatchExportActivityProcessorOptions>>()?.Value?.Should().NotBeNull();
            host.Services.GetService<TracerProvider>().Should().NotBeNull();

            var logProviders = host.Services.GetServices<ILoggerProvider>();
            logProviders.Should().NotBeNullOrEmpty().And.Contain(p => p is OpenTelemetryLoggerProvider);
        }

        [Fact]
        public void OnEnd_SanitizesTags()
        {
            // Arrange
            var activity = new Activity("TestActivity");
            activity.AddTag("url.query", "?code=secret");
            activity.AddTag("url.full", "https://func.net/api/HttpTrigger?code=secret");

            // Act
            ActivitySanitizingProcessor.Instance.OnEnd(activity);

            // Assert
            Assert.Equal("[Hidden Credential]", activity.GetTagItem("url.query"));
            Assert.Equal("https://func.net/api/HttpTrigger[Hidden Credential]", activity.GetTagItem("url.full"));
        }

        [Fact]
        public void OnEnd_DoesNotSanitizeNonSensitiveTags()
        {
            // Arrange
            var activity = new Activity("TestActivity");
            activity.AddTag("non-sensitive", "data");

            // Act
            ActivitySanitizingProcessor.Instance.OnEnd(activity);

            // Assert
            Assert.Equal("data", activity.GetTagItem("non-sensitive"));
        }

        [Fact]
        public void ResourceDetector_Azure()
        {
            using var envVariables = SetupDefaultEnvironmentVariables();

            FunctionsResourceDetector detector = new FunctionsResourceDetector();
            Resource resource = detector.Detect();

            Assert.Equal("/subscriptions/AAAAA-AAAAA-AAAAA-AAA/resourceGroups/rg/providers/Microsoft.Web/sites/appName",
                resource.Attributes.FirstOrDefault(a => a.Key == "cloud.resource_id").Value);
            Assert.Equal("EastUS", resource.Attributes.FirstOrDefault(a => a.Key == "cloud.region").Value);
            Assert.Equal("staging", resource.Attributes.FirstOrDefault(a => a.Key == "deployment.environment.name").Value);
        }

        [Fact]
        public void ResourceDetector_LocalDevelopment()
        {
            FunctionsResourceDetector detector = new FunctionsResourceDetector();
            Resource resource = detector.Detect();

            Assert.Equal(3, resource.Attributes.Count());
        }

        [Fact]
        public void OpenTelemetryBuilder_InPlaceholderMode()
        {
            IHost host;
            using (new TestScopedEnvironmentVariable(new Dictionary<string, string> { { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" } }))
            {
                host = new HostBuilder()
                    .ConfigureLogging((context, builder) =>
                    {
                        builder.ConfigureOpenTelemetry(context, TelemetryMode.Placeholder);
                    })
                    .ConfigureServices(s =>
                    {
                        s.AddSingleton<IEnvironment>(SystemEnvironment.Instance);
                    })
                    .Build();
            }

            var a = host.Services.GetServices<object>();

            var tracerProvider = host.Services.GetService<TracerProvider>();
            Assert.NotNull(tracerProvider);

            var loggerProvider = host.Services.GetService<ILoggerProvider>();
            Assert.NotNull(loggerProvider);

            var openTelemetryLoggerOptions = host.Services.GetService<IOptions<OpenTelemetryLoggerOptions>>();
            Assert.NotNull(openTelemetryLoggerOptions);
            Assert.True(openTelemetryLoggerOptions.Value.IncludeFormattedMessage);
        }

        [Fact]
        public void OpenTelemetryBuilder_NotInPlaceholderMode()
        {
            IHost host;
            using (new TestScopedEnvironmentVariable(new Dictionary<string, string> { { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0" } }))
            {
                host = new HostBuilder()
                    .ConfigureLogging((context, builder) =>
                    {
                        builder.ConfigureOpenTelemetry(context, TelemetryMode.OpenTelemetry);
                    })
                    .ConfigureServices(s =>
                    {
                        s.AddSingleton<IEnvironment>(SystemEnvironment.Instance);
                    })
                    .Build();
            }

            var a = host.Services.GetServices<object>();

            var tracerProvider = host.Services.GetService<TracerProvider>();
            Assert.Null(tracerProvider);

            var loggerProvider = host.Services.GetService<ILoggerProvider>();
            Assert.Null(loggerProvider);
        }

        [Fact]
        public void HttpFilter_Excludes_Localhost_127001()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "http";
            ctx.Request.Host = new HostString("127.0.0.1");
            ctx.Request.Path = "/api/test";

            var result = OpenTelemetryConfigurationExtensions.HttpFilter(ctx);

            Assert.False(result);
        }

        [Fact]
        public void HttpFilter_Excludes_GET_Admin_Health_SubPath()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "http";
            ctx.Request.Host = new HostString("example.com");
            ctx.Request.Method = HttpMethods.Get;
            ctx.Request.Path = "/admin/health/ready";

            var result = OpenTelemetryConfigurationExtensions.HttpFilter(ctx);

            Assert.False(result);
        }

        [Theory]
        [InlineData("/admin/host/synctriggers", "POST", false)]
        [InlineData("/admin/warmup", "GET", false)]
        [InlineData("/admin/host/status", "GET", false)]
        [InlineData("/admin/health", "GET", false)]
        [InlineData("/admin/health/a", "GET", false)]
        [InlineData("/admin/host/ping", "GET", false)]
        [InlineData("/api/myfunction", "GET", true)]
        [InlineData("/api/myfunction", "POST", true)]
        [InlineData("/admin/extensions", "GET", true)]
        public void HttpFilter_ReturnsExpectedResult_ForVariousEndpoints(string path, string method, bool expectedResult)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "http";
            ctx.Request.Host = new HostString("example.com");
            ctx.Request.Method = method;
            ctx.Request.Path = path;

            var result = OpenTelemetryConfigurationExtensions.HttpFilter(ctx);

            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public void EnrichHttpResponse_AddsOnlyFaasTrigger_When_RoutingFeature_Is_Null()
        {
            using var activity = new Activity("HTTP GET");
            activity.Start();

            var ctx = new DefaultHttpContext();

            OpenTelemetryConfigurationExtensions.EnrichHttpResponse(activity, ctx.Response);

            var faasTrigger = activity.Tags.Single(t => t.Key == ResourceSemanticConventions.FaaSTrigger).Value;
            Assert.Equal(OpenTelemetryConstants.HttpTriggerType, faasTrigger);

            Assert.DoesNotContain(activity.Tags, t => t.Key == ResourceSemanticConventions.HttpRoute);
            Assert.Equal("HTTP GET", activity.DisplayName);
        }

        [Fact]
        public void EnrichHttpResponse_DoesNotSetRoute_When_Template_Is_NullOrEmpty()
        {
            using var activity = new Activity("HTTP GET");
            activity.Start();

            var ctx = new DefaultHttpContext();

            var routeData = new RouteData();
            var route = new Route(
                    target: new RouteHandler(_ => Task.CompletedTask),
                    routeName: "empty",
                    routeTemplate: string.Empty,       // empty template
                    defaults: null,
                    constraints: null,
                    dataTokens: null,
                    inlineConstraintResolver: new DummyInlineConstraintResolver());

            routeData.Routers.Add(route);

            var routingFeature = new RoutingFeature { RouteData = routeData };
            ctx.Features.Set<IRoutingFeature>(routingFeature);

            OpenTelemetryConfigurationExtensions.EnrichHttpResponse(activity, ctx.Response);

            var faasTrigger = activity.Tags.Single(t => t.Key == ResourceSemanticConventions.FaaSTrigger).Value;
            Assert.Equal(OpenTelemetryConstants.HttpTriggerType, faasTrigger);

            Assert.DoesNotContain(activity.Tags, t => t.Key == ResourceSemanticConventions.HttpRoute);
            Assert.Equal("HTTP GET", activity.DisplayName);
        }

        [Fact]
        public void EnrichHttpResponse_DoesNotSetRoute_When_Route_Is_NullOrEmpty()
        {
            using var activity = new Activity("HTTP GET");
            activity.Start();

            var ctx = new DefaultHttpContext();

            var routeData = new RouteData();

            var routingFeature = new RoutingFeature { RouteData = routeData };
            ctx.Features.Set<IRoutingFeature>(routingFeature);

            OpenTelemetryConfigurationExtensions.EnrichHttpResponse(activity, ctx.Response);

            var faasTrigger = activity.Tags.Single(t => t.Key == ResourceSemanticConventions.FaaSTrigger).Value;
            Assert.Equal(OpenTelemetryConstants.HttpTriggerType, faasTrigger);

            Assert.DoesNotContain(activity.Tags, t => t.Key == ResourceSemanticConventions.HttpRoute);
            Assert.Equal("HTTP GET", activity.DisplayName);
        }

        [Fact]
        public void EnrichHttpResponse_Sets_Tags_And_DisplayName_For_Valid_Template()
        {
            using var activity = new Activity("HTTP GET");
            activity.Start();

            var ctx = new DefaultHttpContext();

            var routeData = new RouteData();
            var route = new Route(
                target: new RouteHandler(_ => Task.CompletedTask),
                routeName: "hello",
                routeTemplate: "/api/hello/{name}",
                defaults: null,
                constraints: null,
                dataTokens: null,
                inlineConstraintResolver: new DummyInlineConstraintResolver());

            routeData.Routers.Add(route);

            var routingFeature = new RoutingFeature { RouteData = routeData };
            ctx.Features.Set<IRoutingFeature>(routingFeature);

            OpenTelemetryConfigurationExtensions.EnrichHttpResponse(activity, ctx.Response);

            var faasTrigger = activity.Tags.Single(t => t.Key == ResourceSemanticConventions.FaaSTrigger).Value;
            Assert.Equal(OpenTelemetryConstants.HttpTriggerType, faasTrigger);

            var httpRoute = activity.Tags.Single(t => t.Key == ResourceSemanticConventions.HttpRoute).Value;
            Assert.Equal("/api/hello/{name}", httpRoute);

            Assert.Contains("/api/hello/{name}", activity.DisplayName);
        }

        // The OpenTelemetryEventListener is fine because it's a no-op if there are no otel events to listen to
        private bool HasOtelServices(IServiceCollection sc) => sc.Any(sd => sd.ServiceType != typeof(OpenTelemetryEventListener) && sd.ServiceType.FullName.Contains("OpenTelemetry"));

        private static IDisposable SetupDefaultEnvironmentVariables()
        {
            return new TestScopedEnvironmentVariable(new Dictionary<string, string>
            {
                { "WEBSITE_SITE_NAME", "appName" },
                { "WEBSITE_RESOURCE_GROUP", "rg" },
                { "WEBSITE_OWNER_NAME", "AAAAA-AAAAA-AAAAA-AAA+appName-EastUSwebspace" },
                { "REGION_NAME", "EastUS" },
                { "WEBSITE_SLOT_NAME", "staging" }
            });
        }

        private sealed class DummyInlineConstraintResolver : IInlineConstraintResolver
        {
            public IRouteConstraint ResolveConstraint(string inlineConstraint)
            {
                // For tests, you probably don't use constraints at all.
                // Returning a no-op or throwing is fine as long as your code never calls this.
                throw new NotImplementedException("Constraints are not used in tests.");
            }
        }
    }
}