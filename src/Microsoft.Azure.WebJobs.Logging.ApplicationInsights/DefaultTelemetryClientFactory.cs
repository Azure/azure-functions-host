// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Logging.ApplicationInsights
{
    /// <summary>
    /// Creates a <see cref="TelemetryClient"/> for use by the <see cref="ApplicationInsightsLogger"/>. 
    /// </summary>
    public class DefaultTelemetryClientFactory : ITelemetryClientFactory
    {
        private readonly string _instrumentationKey;
        private TelemetryConfiguration _config;
        private bool _disposed;
        private Func<string, LogLevel, bool> _filter;

        /// <summary>
        /// Instantiates an instance.
        /// </summary>
        /// <param name="instrumentationKey">The Application Insights instrumentation key.</param>
        public DefaultTelemetryClientFactory(string instrumentationKey, Func<string, LogLevel, bool> filter)
        {
            _instrumentationKey = instrumentationKey;
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

            // TODO: FACAVAL - We're losing a lot of AI capability with the port to core.
            // A lot of the features we use are defined in the WindowsServer package, which is not currently being ported.
            // Woring with brettsam and the AI team to address the gaps.

            // Configure the TelemetryChannel
            ITelemetryChannel channel = CreateTelemetryChannel();

            // call Initialize if available
            if (channel is ITelemetryModule module)
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
            return new InMemoryChannel();
        }

        internal static void AddInitializers(TelemetryConfiguration config)
        {
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
                _disposed = true;
            }
        }
    }
}
