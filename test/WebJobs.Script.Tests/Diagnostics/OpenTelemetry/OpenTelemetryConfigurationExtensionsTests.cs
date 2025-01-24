// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
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

            Assert.Equal(4, resource.Attributes.Count());
        }

        [Fact]
        public void ConfigureTelemetry_Should_UseOpenTelemetryWhenModeSetAndAppInsightsAuthStringClientIdPresent()
        {
            // Arrange
            var clientId = Guid.NewGuid();
            IServiceCollection serviceCollection = default;

            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "APPLICATIONINSIGHTS_AUTHENTICATION_STRING", $"Authorization=AAD;ClientId={clientId}" },
                        { "APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=key" },
                        { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "telemetryMode"), TelemetryMode.OpenTelemetry.ToString() }
                    });
                })
                .ConfigureDefaultTestWebScriptHost()
                .ConfigureLogging((context, loggingBuilder) => loggingBuilder.ConfigureTelemetry(context))
                .ConfigureServices(services => serviceCollection = services);

            using var host = hostBuilder.Build();

            // Act
            var tracerProviderDescriptors = GetTracerProviderDescriptors(serviceCollection);
            var resolvedClient = ExtractClientFromDescriptors(tracerProviderDescriptors);

            // Extract the clientId from the client object
            var clientIdValue = resolvedClient?.GetType().GetProperty("ClientId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(resolvedClient)?.ToString();

            // Assert
            serviceCollection.Should().NotBeNullOrEmpty();
            clientIdValue.Should().Be(clientId.ToString());
            resolvedClient.GetType().Name.Should().Be("ManagedIdentityClient");
        }

        [Fact]
        public void ConfigureTelemetry_Should_UseOpenTelemetryWhenModeSetAndAppInsightsAuthStringPresent()
        {
            // Arrange
            IServiceCollection serviceCollection = default;

            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "APPLICATIONINSIGHTS_AUTHENTICATION_STRING", $"Authorization=AAD" },
                        { "APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=key" },
                        { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "telemetryMode"), TelemetryMode.OpenTelemetry.ToString() }
                    });
                })
                .ConfigureDefaultTestWebScriptHost()
                .ConfigureLogging((context, loggingBuilder) => loggingBuilder.ConfigureTelemetry(context))
                .ConfigureServices(services => serviceCollection = services);

            using var host = hostBuilder.Build();

            // Act
            var tracerProviderDescriptors = GetTracerProviderDescriptors(serviceCollection);
            var resolvedClient = ExtractClientFromDescriptors(tracerProviderDescriptors);

            // Extract the clientId from the client object
            var clientIdValue = resolvedClient?.GetType().GetProperty("ClientId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(resolvedClient)?.ToString();

            // Assert
            serviceCollection.Should().NotBeNullOrEmpty();
            // No clientId should be present as it was not provided
            clientIdValue.Should().BeNull();
            resolvedClient.GetType().Name.Should().Be("ManagedIdentityClient");
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

        private static List<ServiceDescriptor> GetTracerProviderDescriptors(IServiceCollection services)
        {
            return services
                .Where(descriptor =>
                    descriptor.Lifetime == ServiceLifetime.Singleton &&
                    descriptor.ServiceType.Name == "IConfigureTracerProviderBuilder" &&
                    descriptor.ImplementationInstance?.GetType().Name == "ConfigureTracerProviderBuilderCallbackWrapper")
                .ToList();
        }

        private static object ExtractClientFromDescriptors(List<ServiceDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
            {
                var implementation = descriptor.ImplementationInstance;
                if (implementation is null)
                {
                    continue;
                }

                // Reflection starts here
                var configureField = implementation.GetType().GetField("configure", BindingFlags.Instance | BindingFlags.NonPublic);
                if (configureField?.GetValue(implementation) is Action<IServiceProvider, TracerProviderBuilder> configureDelegate)
                {
                    var targetType = configureDelegate.Target.GetType();
                    var configureDelegateTarget = targetType.GetField("configure", BindingFlags.Instance | BindingFlags.Public);

                    if (configureDelegateTarget?.GetValue(configureDelegate.Target) is Action<AzureMonitorExporterOptions> exporterOptionsDelegate)
                    {
                        var credentialField = exporterOptionsDelegate.Target.GetType().GetField("credential", BindingFlags.Instance | BindingFlags.Public);
                        if (credentialField?.GetValue(exporterOptionsDelegate.Target) is ManagedIdentityCredential managedIdentityCredential)
                        {
                            var clientProperty = managedIdentityCredential.GetType().GetProperty("Client", BindingFlags.Instance | BindingFlags.NonPublic);
                            return clientProperty?.GetValue(managedIdentityCredential);
                        }
                    }
                }
            }
            return null;
        }
    }
}