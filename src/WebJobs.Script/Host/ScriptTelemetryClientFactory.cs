// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.Azure.WebJobs.Host.Loggers;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    /// <summary>
    /// Overrides the default client creation by adding a custom SdkVersion for backend tracking.
    /// </summary>
    internal class ScriptTelemetryClientFactory : ITelemetryClientFactory
    {
        private const string Unknown = "[Unknown]";

        public TelemetryClient Create(string instrumentationKey, SamplingPercentageEstimatorSettings samplingSettings)
        {
            ITelemetryClientFactory defaultFactory = new DefaultTelemetryClientFactory();
            TelemetryClient client = defaultFactory.Create(instrumentationKey, samplingSettings);

            string assemblyVersion = GetAssemblyFileVersion(typeof(ScriptHost).Assembly);
            client.Context.GetInternalContext().SdkVersion = $"azurefuncs: {assemblyVersion}";

            return client;
        }

        internal static string GetAssemblyFileVersion(Assembly assembly)
        {
            AssemblyFileVersionAttribute fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            return fileVersionAttr?.Version ?? Unknown;
        }
    }
}
