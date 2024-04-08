// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Tracing;
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
        internal static void ConfigureOpenTelemetry(this ILoggingBuilder loggingBuilder, HostBuilderContext context)
        {
            loggingBuilder
                .AddOpenTelemetry(o =>
                {
                    o.SetResourceBuilder(ConfigureResource(ResourceBuilder.CreateDefault()));
                    o.AddOtlpExporter();
                    o.AddAzureMonitorLogExporter();
                    o.IncludeFormattedMessage = true;
                    o.IncludeScopes = false;
                })
                // These are messages piped back to the host from the worker - we don't handle these anymore if the worker has OpenTelemetry enabled.
                // Instead, we expect the user's own code to be logging these where they want them to go.
                .AddFilter<OpenTelemetryLoggerProvider>("Host.*", _ => !ScriptHost.WorkerOpenTelemetryEnabled)
                .AddFilter<OpenTelemetryLoggerProvider>("Function.*", _ => !ScriptHost.WorkerOpenTelemetryEnabled)
                .AddFilter<OpenTelemetryLoggerProvider>("Azure.*", _ => !ScriptHost.WorkerOpenTelemetryEnabled)
                .AddFilter<OpenTelemetryLoggerProvider>("Microsoft.Azure.WebJobs.*", _ => !ScriptHost.WorkerOpenTelemetryEnabled);

            // Azure SDK instrumentation is experimental.
            AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

            loggingBuilder.Services.AddOpenTelemetry()
                .ConfigureResource(r => ConfigureResource(r))
                .WithTracing(b => b
                    .AddSource("Azure.*")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation(o =>
                    {
                        o.FilterHttpRequestMessage = _ =>
                        {
                            Activity activity = Activity.Current?.Parent;
                            return (activity == null || !activity.Source.Name.Equals("Azure.Core.Http")) ? true : false;
                        };
                    })
                    .AddLiveMetrics()
                    .AddAzureMonitorTraceExporter()
                    .AddProcessor(ActivitySanitizingProcessor.Instance)
                    .AddProcessor(TraceFilterProcessor.Instance)
                    .AddOtlpExporter())
                .WithMetrics(b => b
                    .AddMeter("Microsoft.AspNetCore.Hosting") // http server metrics
                    .AddMeter("System.Net.Http") // http client metrics
                    .AddAzureMonitorMetricExporter()
                .AddOtlpExporter());

            string eventLogLevel = GetConfigurationValue(EnvironmentSettingNames.OpenTelemetryEventListenerLogLevel, context.Configuration);
            if (!string.IsNullOrEmpty(eventLogLevel))
            {
                if (Enum.TryParse(eventLogLevel, ignoreCase: true, out EventLevel level))
                {
                    loggingBuilder.Services.AddSingleton<IHostedService>(serviceProvider => new OpenTelemetryEventListenerService(level));
                }
                else
                {
                    throw new InvalidEnumArgumentException($"Invalid `{EnvironmentSettingNames.OpenTelemetryEventListenerLogLevel}`.");
                }
            }
            else
            {
                loggingBuilder.Services.AddSingleton<IHostedService>(serviceProvider => new OpenTelemetryEventListenerService(EventLevel.Warning));
            }

            static ResourceBuilder ConfigureResource(ResourceBuilder r)
            {
                string serviceName = Environment.GetEnvironmentVariable(ResourceAttributeConstants.SiteNameEnvVar) ?? "azureFunctions";
                string version = typeof(ScriptHost).Assembly.GetName().Version.ToString();
                r.AddService(serviceName, serviceVersion: version);
                r.AddDetector(new FunctionsResourceDetector());

                // Set the AI SDK to a key so we know all the telemetry came from the Functions Host
                // NOTE: This ties to \azure-sdk-for-net\sdk\monitor\Azure.Monitor.OpenTelemetry.Exporter\src\Internals\ResourceExtensions.cs :: AiSdkPrefixKey used in CreateAzureMonitorResource()
                r.AddAttributes([
                    new(ResourceAttributeConstants.AttributeSDKPrefix, $@"{ResourceAttributeConstants.SDKPrefix}: {version}"),
                    new(ResourceAttributeConstants.AttributeProcessId, Process.GetCurrentProcess().Id)
                ]);

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