// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Monitor.OpenTelemetry.LiveMetrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
                .AddOpenTelemetry(o =>
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
                })
                .AddDefaultOpenTelemetryFilters();

            // Azure SDK instrumentation is experimental.
            AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

            ConfigureTracing(loggingBuilder, enableOtlp, enableAzureMonitor, azMonConnectionString, credential);

            ConfigureEventLogLevel(loggingBuilder, context.Configuration);
        }

        private static void ConfigureTracing(ILoggingBuilder loggingBuilder, bool enableOtlp, bool enableAzureMonitor, string azMonConnectionString, TokenCredential credential)
        {
            loggingBuilder.Services.AddOpenTelemetry()
                .ConfigureResource(r => ConfigureResource(r))
                .WithTracing(builder =>
                {
                    builder.AddSource("Azure.*")
                           .AddAspNetCoreInstrumentation();

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

        private static ILoggingBuilder AddDefaultOpenTelemetryFilters(this ILoggingBuilder loggingBuilder)
        {
            return loggingBuilder
                // These are messages piped back to the host from the worker - we don't handle these anymore if the worker has OpenTelemetry enabled.
                // Instead, we expect the user's own code to be logging these where they want them to go.
                .AddFilter<OpenTelemetryLoggerProvider>("Function.*", _ => !ScriptHost.WorkerOpenTelemetryEnabled)
                .AddFilter<OpenTelemetryLoggerProvider>("Azure.*", _ => !ScriptHost.WorkerOpenTelemetryEnabled)
                // Host.Results and Host.Aggregator are used to emit metrics, ignoring these categories.
                .AddFilter<OpenTelemetryLoggerProvider>("Host.Results", _ => !ScriptHost.WorkerOpenTelemetryEnabled)
                .AddFilter<OpenTelemetryLoggerProvider>("Host.Aggregator", _ => !ScriptHost.WorkerOpenTelemetryEnabled)
                // Ignoring all Microsoft.Azure.WebJobs.* logs like /getScriptTag and /lock.
                .AddFilter<OpenTelemetryLoggerProvider>("Microsoft.Azure.WebJobs.*", _ => !ScriptHost.WorkerOpenTelemetryEnabled);
        }

        private static void ConfigureEventLogLevel(ILoggingBuilder loggingBuilder, IConfiguration configuration)
        {
            string eventLogLevel = GetConfigurationValue(EnvironmentSettingNames.OpenTelemetryEventListenerLogLevel, configuration);
            EventLevel level = !string.IsNullOrEmpty(eventLogLevel) &&
                               Enum.TryParse(eventLogLevel, ignoreCase: true, out EventLevel parsedLevel)
                               ? parsedLevel
                               : EventLevel.Warning;

            loggingBuilder.Services.AddHostedService(_ => new OpenTelemetryEventListenerService(level));
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