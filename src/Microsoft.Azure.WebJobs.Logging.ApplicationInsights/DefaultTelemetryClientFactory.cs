// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector;
using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.QuickPulse;
using Microsoft.ApplicationInsights.WindowsServer;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Logging.ApplicationInsights
{
    /// <summary>
    /// Creates a <see cref="TelemetryClient"/> for use by the <see cref="ApplicationInsightsLogger"/>. 
    /// </summary>
    public class DefaultTelemetryClientFactory : ITelemetryClientFactory
    {
        private readonly string _instrumentationKey;
        private readonly SamplingPercentageEstimatorSettings _samplingSettings;

        private QuickPulseTelemetryModule _quickPulseModule;
        private PerformanceCollectorModule _perfModule;
        private TelemetryConfiguration _config;
        private bool _disposed;
        private Func<string, LogLevel, bool> _filter;

        /// <summary>
        /// Instantiates an instance.
        /// </summary>
        /// <param name="instrumentationKey">The Application Insights instrumentation key.</param>
        /// <param name="samplingSettings">The <see cref="SamplingPercentageEstimatorSettings"/> to use for configuring adaptive sampling. If null, sampling is disabled.</param>
        /// <param name="filter"></param>
        public DefaultTelemetryClientFactory(string instrumentationKey, SamplingPercentageEstimatorSettings samplingSettings, Func<string, LogLevel, bool> filter)
        {
            _instrumentationKey = instrumentationKey;
            _samplingSettings = samplingSettings;
            _filter = filter;
        }

        /// <summary>
        /// Creates a <see cref="TelemetryClient"/>. 
        /// </summary>
        /// <returns>The <see cref="TelemetryClient"/> instance.</returns>
        public virtual TelemetryClient Create()
        {
            _config = InitializeConfiguration();

            TelemetryClient client = new TelemetryClient(_config);

            string assemblyVersion = GetAssemblyFileVersion(typeof(JobHost).Assembly);
            client.Context.GetInternalContext().SdkVersion = $"webjobs: {assemblyVersion}";

            return client;
        }

        internal static string GetAssemblyFileVersion(Assembly assembly)
        {
            AssemblyFileVersionAttribute fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            return fileVersionAttr?.Version ?? LoggingConstants.Unknown;
        }

        internal TelemetryConfiguration InitializeConfiguration()
        {
            TelemetryConfiguration config = new TelemetryConfiguration()
            {
                InstrumentationKey = _instrumentationKey
            };

            AddInitializers(config);

            // Plug in Live stream and adaptive sampling
            QuickPulseTelemetryProcessor processor = null;
            TelemetryProcessorChainBuilder builder = config.TelemetryProcessorChainBuilder
                .Use((next) =>
                {
                    processor = new QuickPulseTelemetryProcessor(next);
                    return processor;
                })
                .Use((next) =>
                {
                    return new FilteringTelemetryProcessor(_filter, next);
                });

            if (_samplingSettings != null)
            {
                builder.Use((next) =>
                {
                    return new AdaptiveSamplingTelemetryProcessor(_samplingSettings, null, next);
                });
            }

            builder.Build();

            _quickPulseModule = CreateQuickPulseTelemetryModule();
            _quickPulseModule.Initialize(config);
            _quickPulseModule.RegisterTelemetryProcessor(processor);

            // Plug in perf counters
            _perfModule = new PerformanceCollectorModule();
            _perfModule.Initialize(config);

            // Configure the TelemetryChannel
            ITelemetryChannel channel = CreateTelemetryChannel();

            // call Initialize if available
            ITelemetryModule module = channel as ITelemetryModule;
            if (module != null)
            {
                module.Initialize(config);
            }

            config.TelemetryChannel = channel;

            return config;
        }

        /// <summary>
        /// Creates the <see cref="ITelemetryChannel"/> to be used by the <see cref="TelemetryClient"/>. If this channel
        /// implements <see cref="ITelemetryModule"/> as well, <see cref="ITelemetryModule.Initialize(TelemetryConfiguration)"/> will
        /// automatically be called.
        /// </summary>
        /// <returns>The <see cref="ITelemetryChannel"/></returns>
        protected virtual ITelemetryChannel CreateTelemetryChannel()
        {
            return new ServerTelemetryChannel();
        }

        /// <summary>
        /// Creates the <see cref="QuickPulseTelemetryModule"/> to be used by the <see cref="TelemetryClient"/>.
        /// </summary>
        /// <returns>The <see cref="QuickPulseTelemetryModule"/>.</returns>
        protected virtual QuickPulseTelemetryModule CreateQuickPulseTelemetryModule()
        {
            return new QuickPulseTelemetryModule();
        }

        internal static void AddInitializers(TelemetryConfiguration config)
        {
            // This picks up the RoleName from the server
            config.TelemetryInitializers.Add(new AzureWebAppRoleEnvironmentTelemetryInitializer());

            // This applies our special scope properties and gets RoleInstance name
            config.TelemetryInitializers.Add(new WebJobsTelemetryInitializer());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the instance.
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                // TelemetryConfiguration.Dispose will dispose the Channel and the TelemetryProcessors
                // registered with the TelemetryProcessorChainBuilder.
                _config?.Dispose();

                _perfModule?.Dispose();
                _quickPulseModule?.Dispose();

                _disposed = true;
            }
        }
    }
}
