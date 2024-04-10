// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
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
                .ConfigureLogging((ctx, lb) => lb.ConfigureTelemetry(ctx))
                .ConfigureServices(s => sc = s);

            using IHost host = hostBuilder.Build();

            // Assert
            sc.Should().NotBeNullOrEmpty();
            HasOtelServices(sc).Should().BeTrue();
            sc.Should().Contain(sd => sd.ServiceType.FullName == "OpenTelemetry.Trace.IConfigureTracerProviderBuilder");
            sc.Should().Contain(sd => sd.ServiceType.FullName == "OpenTelemetry.Logs.IConfigureLoggerProviderBuilder");

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
        public void ResourceDetectorLocalDevelopment2()
        {
            using var envVariables = SetupDefaultEnvironmentVariables();

            FunctionsResourceDetector detector = new FunctionsResourceDetector();
            Resource resource = detector.Detect();

            Assert.Equal($"/subscriptions/AAAAA-AAAAA-AAAAA-AAA/resourceGroups/rg/providers/Microsoft.Web/sites/appName",
                resource.Attributes.FirstOrDefault(a => a.Key == "cloud.resource.id").Value);
            Assert.Equal($"EastUS", resource.Attributes.FirstOrDefault(a => a.Key == "cloud.region").Value);
        }

        [Fact]
        public void ResourceDetectorLocalDevelopment()
        {
            FunctionsResourceDetector detector = new FunctionsResourceDetector();
            Resource resource = detector.Detect();

            Assert.Equal(3, resource.Attributes.Count());
            var attribute = resource.Attributes.FirstOrDefault(a => a.Key == "cloud.provider");
            Assert.Equal("azure", resource.Attributes.FirstOrDefault(a => a.Key == "cloud.provider").Value);
            Assert.Equal("azure_functions", resource.Attributes.FirstOrDefault(a => a.Key == "cloud.platform").Value);
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
            { "REGION_NAME", "EastUS" }
        });
        }
    }
}