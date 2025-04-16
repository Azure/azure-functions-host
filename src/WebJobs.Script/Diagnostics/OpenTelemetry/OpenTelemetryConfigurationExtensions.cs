// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Monitor.OpenTelemetry.LiveMetrics;
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
            var connectionString = GetConfigurationValue(EnvironmentSettingNames.AppInsightsConnectionString, context.Configuration);
            var (azMonConnectionString, credential, enableOtlp, enableAzureMonitor) = telemetryMode switch
            {
                // Initializing OTel services during placeholder mode as well to avoid the cost of JITting these objects during specialization.
                // Azure Monitor Exporter requires a connection string to be initialized. Use placeholder connection string if in placeholder mode.
                TelemetryMode.Placeholder => (
                    "InstrumentationKey=00000000-0000-0000-0000-000000000000;",
                    null,
                    true,
                    true),
                _ => (
                    connectionString,
                    GetTokenCredential(context.Configuration),
                    !string.IsNullOrEmpty(GetConfigurationValue(EnvironmentSettingNames.OtlpEndpoint, context.Configuration)),
                    !string.IsNullOrEmpty(connectionString))
            };

            // If neither OTLP nor Azure Monitor is enabled, don't configure OpenTelemetry.
            if (!enableOtlp && !enableAzureMonitor)
            {
                return;
            }

            loggingBuilder
                .ConfigureLogging(enableOtlp, enableAzureMonitor, azMonConnectionString, credential).Services
                .AddOpenTelemetry()
                .ConfigureResource(r => ConfigureResource(r))
                .ConfigureMetrics(enableOtlp, enableAzureMonitor, azMonConnectionString, credential)
                .ConfigureTracing(enableOtlp, enableAzureMonitor, azMonConnectionString, credential)
                .ConfigureEventLogLevel(context.Configuration);

            // Azure SDK instrumentation is experimental.
            AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);
        }

        private static IOpenTelemetryBuilder ConfigureMetrics(this IOpenTelemetryBuilder builder, bool enableOtlp, bool enableAzureMonitor, string azMonConnectionString, TokenCredential credential)
        {
            return builder.WithMetrics(builder =>
            {
                builder.AddAspNetCoreInstrumentation();
                builder.AddMeter(HostMetrics.FaasMeterName);
                builder.AddView(HostMetrics.FaasInvokeDuration, new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = new double[] { 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10 }
                });

                if (enableOtlp)
                {
                    builder.AddOtlpExporter();
                }
                if (enableAzureMonitor)
                {
                    builder.AddAzureMonitorMetricExporter(opt => ConfigureAzureMonitorOptions(opt, azMonConnectionString, credential));
                }
            });
        }

        private static IOpenTelemetryBuilder ConfigureTracing(this IOpenTelemetryBuilder builder, bool enableOtlp, bool enableAzureMonitor, string azMonConnectionString, TokenCredential credential)
        {
            return builder.WithTracing(builder =>
            {
                builder.AddSource("Azure.*")
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
                });

                if (enableOtlp)
                {
                    builder.AddOtlpExporter();
                }

                if (enableAzureMonitor)
                {
                    builder.AddAzureMonitorTraceExporter(opt => ConfigureAzureMonitorOptions(opt, azMonConnectionString, credential));
                    builder.AddLiveMetrics(opt => ConfigureAzureMonitorOptions(opt, azMonConnectionString, credential));
                }

                builder.AddProcessor(ActivitySanitizingProcessor.Instance);
            });
        }

        private static ILoggingBuilder ConfigureLogging(this ILoggingBuilder builder, bool enableOtlp, bool enableAzureMonitor, string azMonConnectionString, TokenCredential credential)
        {
            builder.AddOpenTelemetry(o =>
            {
                o.SetResourceBuilder(ConfigureResource(ResourceBuilder.CreateDefault()));
                if (enableOtlp)
                {
                    o.AddOtlpExporter();
                }
                if (enableAzureMonitor)
                {
                    o.AddAzureMonitorLogExporter(options => ConfigureAzureMonitorOptions(options, azMonConnectionString, credential));
                }
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

        private static void ConfigureAzureMonitorOptions(LiveMetricsExporterOptions options, string connectionString, TokenCredential credential)
        {
            options.ConnectionString = connectionString;
            if (credential is not null)
            {
                options.Credential = credential;
            }
        }
    }
}