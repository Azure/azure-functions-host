// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Azure.Monitor.OpenTelemetry.Exporter;
using FluentAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.OpenTelemetry
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
                        { "APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=cd2a2a9f-f044-4568-a294-87a636e54fcf;IngestionEndpoint=https://westus2-2.in.applicationinsights.azure.com/;LiveEndpoint=https://westus2.livediagnostics.monitor.azure.com/" },
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

        // The OpenTelemetryEventListener is fine because it's a no-op if there are no otel events to listen to
        private bool HasOtelServices(IServiceCollection sc) => sc.Any(sd => sd.ServiceType != typeof(OpenTelemetryEventListener) && sd.ServiceType.FullName.Contains("OpenTelemetry"));
    }
}