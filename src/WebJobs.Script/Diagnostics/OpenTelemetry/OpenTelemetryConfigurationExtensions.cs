// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Monitor.OpenTelemetry.LiveMetrics;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal static class OpenTelemetryConfigurationExtensions
    {
        public static void ConfigureOpenTelemetry(this ILoggingBuilder loggingBuilder)
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

            loggingBuilder.Services.AddSingleton<IHostedService>(serviceProvider => new OpenTelemetryEventListenerService(EventLevel.Informational));

            static ResourceBuilder ConfigureResource(ResourceBuilder r)
            {
                string serviceName = Environment.GetEnvironmentVariable(ResourceAttributeConstants.SiteNameEnvVar) ?? "azureFunctions";
                string version = typeof(ScriptHost).Assembly.GetName().Version.ToString();
                r.AddService(serviceName, serviceVersion: version);

                // Set the AI SDK to a key so we know all the telemetry came from the Functions Host
                // NOTE: This ties to \azure-sdk-for-net\sdk\monitor\Azure.Monitor.OpenTelemetry.Exporter\src\Internals\ResourceExtensions.cs :: AiSdkPrefixKey used in CreateAzureMonitorResource()
                r.AddAttributes([
                    new(ResourceAttributeConstants.AttributeSDKPrefix, $@"{ResourceAttributeConstants.SDKPrefix}: {version}"),
                    new(ResourceAttributeConstants.AttributeProcessId, Process.GetCurrentProcess().Id)
                ]);

                return r;
            }
        }

        public static void AddOpenTelemetryConfigurations(this IConfigurationBuilder configBuilder, HostBuilderContext context)
        {
            // .NET would pick up otel config automatically if we stored it at <root> { Metrics { ...
            // but we've chosen to put it at <root> { openTelemetry { Metrics { ...
            // so, we need to change its structure accordingly, then manually add it to the config builder so .NET picks it up as though it were in the format expected
            var customOtelSection = context.Configuration.GetSection(ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, OpenTelemetryConstants.Metrics, OpenTelemetryConstants.OpenTelemetry));
            if (customOtelSection.Exists())
            {
                customOtelSection = TranslateEnabledMetricValues(customOtelSection);

                // Create a new configuration that removes the 'openTelemetry' layer
                var newConfigBuilder = new ConfigurationBuilder();
                newConfigBuilder.AddInMemoryCollection([new(OpenTelemetryConstants.Metrics, customOtelSection.Value)]);

                configBuilder.AddConfiguration(newConfigBuilder.Build());
            }

            // Do the same with 'traces' section
            customOtelSection = context.Configuration.GetSection(ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, OpenTelemetryConstants.Traces, OpenTelemetryConstants.OpenTelemetry));
            if (customOtelSection.Exists())
            {
                customOtelSection = TranslateEnabledTraceValues(customOtelSection);

                // Create a new configuration that removes the 'openTelemetry' layer
                var newConfigBuilder = new ConfigurationBuilder();
                newConfigBuilder.AddInMemoryCollection([new(OpenTelemetryConstants.Traces, customOtelSection.Value)]);

                configBuilder.AddConfiguration(newConfigBuilder.Build());
            }
        }

        /// <summary>
        /// Translates the enabled metric values in the specified custom OpenTelemetry section.
        /// </summary>
        /// <param name="customOtelSection">The custom OpenTelemetry section.</param>
        /// <returns>The translated custom OpenTelemetry section.</returns>
        private static IConfigurationSection TranslateEnabledMetricValues(IConfigurationSection customOtelSection)
        {
            // if there's an entry under 'enabledMetrics' with a key of 'Functions.Runtime', rename it to 'Microsoft.AspNet.Core'
            var enabledMetrics = customOtelSection.GetSection(OpenTelemetryConstants.EnabledMetrics);
            if (enabledMetrics.Exists())
            {
                var functionsRuntime = enabledMetrics.GetSection(OpenTelemetryConstants.FunctionsRuntimeMetrics);
                if (functionsRuntime.Exists())
                {
                    enabledMetrics["Microsoft.AspNet.Core"] = functionsRuntime.Value;
                    enabledMetrics.GetSection(OpenTelemetryConstants.FunctionsRuntimeMetrics).Value = null;
                }
            }

            return customOtelSection;
        }

        /// <summary>
        /// Translates the enabled trace values in the customOtelSection configuration.
        /// If there is an entry under 'enabledTraces' with a key of 'FunctionsRuntimeInstrumentation',
        /// it renames it to 'AspNetCoreInstrumentation'.
        /// </summary>
        private static IConfigurationSection TranslateEnabledTraceValues(IConfigurationSection customOtelSection)
        {
            // if there's an entry under 'enabledTraces' with a key of 'FunctionsRuntimeInstrumentation', rename it to 'AspNetCoreInstrumentation'
            var enabledTraces = customOtelSection.GetSection(OpenTelemetryConstants.EnabledTraces);
            if (enabledTraces.Exists())
            {
                var functionsRuntimeInstrumentation = enabledTraces.GetSection(OpenTelemetryConstants.FunctionsRuntimeInstrumentationTraces);
                if (functionsRuntimeInstrumentation.Exists())
                {
                    enabledTraces["AspNetCoreInstrumentation"] = functionsRuntimeInstrumentation.Value;
                    enabledTraces.GetSection(OpenTelemetryConstants.FunctionsRuntimeInstrumentationTraces).Value = null;
                }
            }

            return customOtelSection;
        }
    }
}