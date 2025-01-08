// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.ComponentModel;
using System.Diagnostics.Tracing;
using System.Linq;
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

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal static class OpenTelemetryConfigurationExtensions
    {
        private static readonly string[] ExcludedRequestSubstrings =
        [
            "azure-webjobs-hosts",
            "azureFunctionsRpcMessages"
        ];

        internal static void ConfigureOpenTelemetry(this ILoggingBuilder loggingBuilder, HostBuilderContext context)
        {
            // Initializing OTel services during placeholder mode as well to avoid the cost of JITting these objects during specialization.
            bool isPlaceholderMode = SystemEnvironment.Instance.IsPlaceholderModeEnabled();
            bool enableOtlp = isPlaceholderMode ||
                             !string.IsNullOrEmpty(GetConfigurationValue(EnvironmentSettingNames.OtlpEndpoint, context.Configuration));

            // Azure Monitor Exporter requires a connection string to be initialized. Use placeholder connection string accordingly.
            string azMonConnectionString = isPlaceholderMode
                ? "InstrumentationKey=00000000-0000-0000-0000-000000000000;"
                : GetConfigurationValue(EnvironmentSettingNames.AppInsightsConnectionString, context.Configuration);
            bool enableAzureMonitor = !string.IsNullOrEmpty(azMonConnectionString);

            if (!isPlaceholderMode && !enableOtlp && !enableAzureMonitor)
            {
                // Skip OpenTelemetry configuration if OTLP and Azure Monitor are both disabled and not in placeholder mode.
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
                        o.AddAzureMonitorLogExporter(options => options.ConnectionString = azMonConnectionString);
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
                        o.FilterHttpRequestMessage = static (httpRequestMessage) =>
                        {
                            if (httpRequestMessage.RequestUri?.AbsoluteUri is not { Length: > 0 } uri)
                            {
                                return false;
                            }

                            foreach (string substring in ExcludedRequestSubstrings)
                            {
                                if (uri.IndexOf(substring, StringComparison.Ordinal) >= 0)
                                {
                                    return false;
                                }
                            }

                            return true;
                        };
                    });
                    if (enableOtlp)
                    {
                        b.AddOtlpExporter();
                    }
                    if (enableAzureMonitor)
                    {
                        b.AddAzureMonitorTraceExporter(options => options.ConnectionString = azMonConnectionString);
                        b.AddLiveMetrics(options => options.ConnectionString = azMonConnectionString);
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
    }
}