// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.ComponentModel;
using System.Diagnostics;
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
        internal static void ConfigureOpenTelemetry(this ILoggingBuilder loggingBuilder, HostBuilderContext context)
        {
            string azMonConnectionString = GetConfigurationValue(EnvironmentSettingNames.AppInsightsConnectionString, context.Configuration);
            TokenCredential credential = GetTokenCredential(context.Configuration);

            bool enableOtlp = false;
            if (!string.IsNullOrEmpty(GetConfigurationValue(EnvironmentSettingNames.OtlpEndpoint, context.Configuration)))
            {
                enableOtlp = true;
            }

            loggingBuilder
                .AddOpenTelemetry(o =>
                {
                    o.SetResourceBuilder(ConfigureResource(ResourceBuilder.CreateDefault()));
                    if (enableOtlp)
                    {
                        o.AddOtlpExporter();
                    }
                    if (!string.IsNullOrEmpty(azMonConnectionString))
                    {
                        o.AddAzureMonitorLogExporter(options => ConfigureAzureMonitorOptions(options, azMonConnectionString, credential));
                    }
                    o.IncludeFormattedMessage = true;
                    o.IncludeScopes = false;
                })
                // These are messages piped back to the host from the worker - we don't handle these anymore if the worker has OpenTelemetry enabled.
                // Instead, we expect the user's own code to be logging these where they want them to go.
                .AddFilter<OpenTelemetryLoggerProvider>("Function.*", _ => !ScriptHost.WorkerOpenTelemetryEnabled)
                .AddFilter<OpenTelemetryLoggerProvider>("Azure.*", _ => !ScriptHost.WorkerOpenTelemetryEnabled)
                // Host.Results and Host.Aggregator are used to emit metrics, ignoring these categories.
                .AddFilter<OpenTelemetryLoggerProvider>("Host.Results", _ => !ScriptHost.WorkerOpenTelemetryEnabled)
                .AddFilter<OpenTelemetryLoggerProvider>("Host.Aggregator", _ => !ScriptHost.WorkerOpenTelemetryEnabled)
                // Ignoring all Microsoft.Azure.WebJobs.* logs like /getScriptTag and /lock.
                .AddFilter<OpenTelemetryLoggerProvider>("Microsoft.Azure.WebJobs.*", _ => !ScriptHost.WorkerOpenTelemetryEnabled);

            // Azure SDK instrumentation is experimental.
            AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

            loggingBuilder.Services.AddOpenTelemetry()
                .ConfigureResource(r => ConfigureResource(r))
                .WithTracing(b =>
                {
                    b.AddSource("Azure.*");
                    b.AddAspNetCoreInstrumentation();
                    b.AddHttpClientInstrumentation(o =>
                    {
                        o.FilterHttpRequestMessage = _ =>
                        {
                            Activity activity = Activity.Current?.Parent;
                            return activity == null || !activity.Source.Name.Equals("Azure.Core.Http");
                        };
                    });

                    if (enableOtlp)
                    {
                        b.AddOtlpExporter();
                    }

                    if (!string.IsNullOrEmpty(azMonConnectionString))
                    {
                        b.AddAzureMonitorTraceExporter(options => ConfigureAzureMonitorOptions(options, azMonConnectionString, credential));
                        b.AddLiveMetrics(options => ConfigureAzureMonitorOptions(options, azMonConnectionString, credential));
                    }

                    b.AddProcessor(ActivitySanitizingProcessor.Instance);
                    b.AddProcessor(TraceFilterProcessor.Instance);
                });

            string eventLogLevel = GetConfigurationValue(EnvironmentSettingNames.OpenTelemetryEventListenerLogLevel, context.Configuration);
            if (!string.IsNullOrEmpty(eventLogLevel))
            {
                if (Enum.TryParse(eventLogLevel, ignoreCase: true, out EventLevel level))
                {
                    loggingBuilder.Services.AddHostedService(service => new OpenTelemetryEventListenerService(level));
                }
                else
                {
                    throw new InvalidEnumArgumentException($"Invalid '{EnvironmentSettingNames.OpenTelemetryEventListenerLogLevel}' of '{eventLogLevel}'.");
                }
            }
            else
            {
                // Log all warnings and above by default.
                loggingBuilder.Services.AddHostedService(service => new OpenTelemetryEventListenerService(EventLevel.Warning));
            }

            static ResourceBuilder ConfigureResource(ResourceBuilder r)
            {
                r.AddDetector(new FunctionsResourceDetector());

                // Set the AI SDK to a key so we know all the telemetry came from the Functions Host
                // NOTE: This ties to \azure-sdk-for-net\sdk\monitor\Azure.Monitor.OpenTelemetry.Exporter\src\Internals\ResourceExtensions.cs :: AiSdkPrefixKey used in CreateAzureMonitorResource()
                return r;
            }
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