﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Overrides the default client creation by adding a custom SdkVersion for backend tracking.
    /// </summary>
    internal class ScriptTelemetryClientFactory : DefaultTelemetryClientFactory
    {
        public ScriptTelemetryClientFactory(string instrumentationKey, SamplingPercentageEstimatorSettings samplingSettings, Func<string, LogLevel, bool> filter)
            : base(instrumentationKey, samplingSettings, filter)
        {
        }

        public ScriptTelemetryClientFactory(
                    string instrumentationKey,
                    SamplingPercentageEstimatorSettings samplingSettings,
                    string ingestionEndpoint,
                    string liveEndpoint,
                    Func<string, LogLevel, bool> filter)
            : base(
                  instrumentationKey,
                  samplingSettings,
                  ingestionEndpoint,
                  liveEndpoint,
                  filter)
        {
        }

        public override TelemetryClient Create()
        {
            TelemetryClient client = base.Create();

            string assemblyVersion = ScriptHost.GetAssemblyFileVersion(typeof(ScriptHost).Assembly);
            client.Context.GetInternalContext().SdkVersion = $"azurefunctions: {assemblyVersion}";

            return client;
        }
    }
}
