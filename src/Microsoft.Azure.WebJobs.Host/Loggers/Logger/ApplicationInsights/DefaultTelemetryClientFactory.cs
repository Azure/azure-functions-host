// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector;
using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.QuickPulse;
using Microsoft.ApplicationInsights.WindowsServer;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    /// <summary>
    /// Creates a <see cref="TelemetryClient"/> for use by the <see cref="ApplicationInsightsLogger"/>. 
    /// </summary>
    [CLSCompliant(false)]
    public class DefaultTelemetryClientFactory : ITelemetryClientFactory
    {
        /// <summary>
        /// Creates a <see cref="TelemetryClient"/>. 
        /// </summary>
        /// <returns>The <see cref="TelemetryClient"/> instance.</returns>
        public TelemetryClient Create(string instrumentationKey, SamplingPercentageEstimatorSettings samplingSettings)
        {
            TelemetryConfiguration config = InitializeConfiguration(instrumentationKey, samplingSettings);

            TelemetryClient client = new TelemetryClient(config);

            string assemblyVersion = GetAssemblyFileVersion(typeof(JobHost).Assembly);
            client.Context.GetInternalContext().SdkVersion = $"webjobs: {assemblyVersion}";

            return client;
        }

        internal static string GetAssemblyFileVersion(Assembly assembly)
        {
            AssemblyFileVersionAttribute fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            return fileVersionAttr?.Version ?? LoggingConstants.Unknown;
        }

        internal static TelemetryConfiguration InitializeConfiguration(string instrumentationKey,
            SamplingPercentageEstimatorSettings samplingSettings)
        {
            TelemetryConfiguration config = new TelemetryConfiguration();
            config.InstrumentationKey = instrumentationKey;

            AddInitializers(config);

            // Plug in Live stream and adaptive sampling
            QuickPulseTelemetryProcessor processor = null;
            TelemetryProcessorChainBuilder builder = config.TelemetryProcessorChainBuilder
                .Use((next) =>
                {
                    processor = new QuickPulseTelemetryProcessor(next);
                    return processor;
                });

            if (samplingSettings != null)
            {
                builder.Use((next) =>
                {
                    return new AdaptiveSamplingTelemetryProcessor(samplingSettings, null, next);
                });
            }

            builder.Build();

            QuickPulseTelemetryModule quickPulse = new QuickPulseTelemetryModule();
            quickPulse.Initialize(config);
            quickPulse.RegisterTelemetryProcessor(processor);

            // Plug in perf counters
            PerformanceCollectorModule perfCounterCollectorModule = new PerformanceCollectorModule();
            perfCounterCollectorModule.Initialize(config);

            ServerTelemetryChannel channel = new ServerTelemetryChannel();
            channel.Initialize(config);
            config.TelemetryChannel = channel;

            return config;
        }

        internal static void AddInitializers(TelemetryConfiguration config)
        {
            // This picks up the RoleName from the server
            config.TelemetryInitializers.Add(new AzureWebAppRoleEnvironmentTelemetryInitializer());

            // This applies our special scope properties and gets RoleInstance name
            config.TelemetryInitializers.Add(new WebJobsTelemetryInitializer());
        }
    }
}
