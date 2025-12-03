// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using AppInsightsCredentialOptions = Microsoft.Azure.WebJobs.Logging.ApplicationInsights.TokenCredentialOptions;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal static class OpenTelemetryConfigurationExtensions
    {
        internal static void ConfigureOpenTelemetry(this ILoggingBuilder loggingBuilder, HostBuilderContext context, TelemetryMode telemetryMode)
        {
            var otlpEndpoint = GetConfigurationValue(EnvironmentSettingNames.OtlpEndpoint, context.Configuration);
            var azMonConnectionString = GetConfigurationValue(EnvironmentSettingNames.AppInsightsConnectionString, context.Configuration);

            bool enableOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);
            bool enableAzureMonitor = !string.IsNullOrWhiteSpace(azMonConnectionString);

            // If placeholder mode is disabled and both OTLP and Azure Monitor are not enabled, avoid configuring OpenTelemetry.
            if (!enableOtlp && !enableAzureMonitor && telemetryMode != TelemetryMode.Placeholder)
            {
                return;
            }

            loggingBuilder.ConfigureLogging();

            loggingBuilder.Services
                .AddOpenTelemetry()
                .ConfigureExporters(context.Configuration, enableOtlp, enableAzureMonitor, azMonConnectionString, telemetryMode)
                .ConfigureResource(r => ConfigureResource(r))
                .ConfigureMetrics()
                .ConfigureTracing()
                .ConfigureEventLogLevel(context.Configuration);

            // Azure SDK instrumentation is experimental.
            AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);
        }

        private static IOpenTelemetryBuilder ConfigureExporters(this IOpenTelemetryBuilder builder, IConfiguration configuration, bool enableOtlp, bool enableAzureMonitor, string azMonConnectionString, TelemetryMode telemetryMode)
        {
            // Avoid configuring the exporter in placeholder mode, as it will default to sending telemetry to the predefined endpoint. These transmissions will be unsuccessful and create unnecessary noise.
            if (telemetryMode == TelemetryMode.Placeholder)
            {
                return builder;
            }

            if (enableOtlp)
            {
                builder.UseOtlpExporter();
            }

            if (enableAzureMonitor)
            {
                TokenCredential credential = GetTokenCredential(configuration);
                builder.UseAzureMonitorExporter(options => ConfigureAzureMonitorOptions(options, azMonConnectionString, credential));
            }

            return builder;
        }

        private static IOpenTelemetryBuilder ConfigureMetrics(this IOpenTelemetryBuilder builder)
        {
            return builder.WithMetrics(builder =>
            {
                builder.AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddMeter(HostMetrics.FaasMeterName)
                    .AddView(HostMetrics.FaasInvokeDuration, new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = new double[] { 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10 },
                        RecordMinMax = true
                    });
            });
        }

        private static IOpenTelemetryBuilder ConfigureTracing(this IOpenTelemetryBuilder builder)
        {
            return builder.WithTracing(builder =>
            {
                builder
                    .AddSource("Azure.Messaging.ServiceBus.ServiceBusProcessor")
                    .AddSource("Azure.Messaging.EventHubs.EventProcessor")
                    .AddSource("Azure.Functions.Extensions.Mcp")
                    .AddSource("Microsoft.Azure.WebJobs.Extensions.*")
                    .AddSource("Microsoft.Azure.WebJobs")
                    .AddSource("WebJobs.Extensions.DurableTask")
                    .AddSource("DurableTask.*")
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.EnrichWithHttpResponse = (activity, httpResponse) =>
                        {
                            if (Activity.Current is not null)
                            {
                                Activity.Current.AddTag(ResourceSemanticConventions.FaaSTrigger, OpenTelemetryConstants.HttpTriggerType);

                                var routingFeature = httpResponse.HttpContext.Features.Get<IRoutingFeature>();
                                if (routingFeature is null)
                                {
                                    return;
                                }

                                var template = routingFeature.RouteData.Routers.FirstOrDefault(r => r is Route) as Route;
                                Activity.Current.DisplayName = $"{Activity.Current.DisplayName} {template?.RouteTemplate}";
                                Activity.Current.AddTag(ResourceSemanticConventions.HttpRoute, template?.RouteTemplate);
                            }
                        };
                        o.Filter = context =>
                        {
                            // Exclude localhost calls
                            if (context.Request.Host.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }

                            // Exclude POST /admin/host/synctriggers
                            if (string.Equals(context.Request.Method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase)
                                && context.Request.Path.Equals("/admin/host/synctriggers", StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }

                            // Exclude GET /admin/warmup
                            if (string.Equals(context.Request.Method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)
                                && context.Request.Path.Equals("/admin/warmup", StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }

                            // Exclude GET /admin/host/status
                            if (string.Equals(context.Request.Method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)
                                && context.Request.Path.Equals("/admin/host/status", StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }

                            // Exclude GET /admin/health and its sub-paths
                            if (string.Equals(context.Request.Method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)
                                && context.Request.Path.StartsWithSegments("/admin/health", StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }

                            // Allow everything else
                            return true;
                        };
                    })
                    .AddProcessor(ActivitySanitizingProcessor.Instance);
            });
        }

        private static ILoggingBuilder ConfigureLogging(this ILoggingBuilder builder)
        {
            builder.AddOpenTelemetry(o =>
            {
                o.SetResourceBuilder(ConfigureResource(ResourceBuilder.CreateDefault()));
                o.IncludeFormattedMessage = true;
                o.IncludeScopes = false;
            });
            builder.AddDefaultOpenTelemetryFilters();

            return builder;
        }

        private static ILoggingBuilder AddDefaultOpenTelemetryFilters(this ILoggingBuilder loggingBuilder)
        {
            return loggingBuilder
                // These are messages piped back to the host from the worker - we don't handle these anymore if the worker has OpenTelemetry enabled.
                // Instead, we expect the user's own code to be logging these where they want them to go.
                .AddFilter<OpenTelemetryLoggerProvider>("Function.*", _ => !ScriptHost.WorkerOpenTelemetryEnabled)

                // Always filter out these logs
                .AddFilter<OpenTelemetryLoggerProvider>("Azure.*", _ => false)
                // Host.Results and Host.Aggregator are used to emit metrics, ignoring these categories.
                .AddFilter<OpenTelemetryLoggerProvider>("Host.Results", _ => false)
                .AddFilter<OpenTelemetryLoggerProvider>("Host.Aggregator", _ => false);
        }

        private static IOpenTelemetryBuilder ConfigureEventLogLevel(this IOpenTelemetryBuilder builder, IConfiguration configuration)
        {
            string eventLogLevel = GetConfigurationValue(EnvironmentSettingNames.OpenTelemetryEventListenerLogLevel, configuration);
            EventLevel level = !string.IsNullOrEmpty(eventLogLevel) &&
                               Enum.TryParse(eventLogLevel, ignoreCase: true, out EventLevel parsedLevel)
                               ? parsedLevel
                               : EventLevel.Warning;

            builder.Services.AddHostedService(_ => new OpenTelemetryEventListenerService(level));

            return builder;
        }

        private static ResourceBuilder ConfigureResource(ResourceBuilder builder)
        {
            return builder.AddDetector(new FunctionsResourceDetector());
        }

        private static string GetConfigurationValue(string key, IConfiguration configuration = null)
        {
            if (configuration != null && configuration[key] is string configValue)
            {
                return configValue;
            }
            else if (Environment.GetEnvironmentVariable(key) is string envValue)
            {
                return envValue;
            }
            else
            {
                return null;
            }
        }

        private static TokenCredential GetTokenCredential(IConfiguration configuration)
        {
            if (GetConfigurationValue(EnvironmentSettingNames.AppInsightsAuthenticationString, configuration) is string authString)
            {
                AppInsightsCredentialOptions credOptions = AppInsightsCredentialOptions.ParseAuthenticationString(authString);
                return new ManagedIdentityCredential(credOptions.ClientId);
            }

            return null;
        }

        private static void ConfigureAzureMonitorOptions(AzureMonitorExporterOptions options, string connectionString, TokenCredential credential)
        {
            options.ConnectionString = connectionString;
            if (credential is not null)
            {
                options.Credential = credential;
            }
        }
    }
}