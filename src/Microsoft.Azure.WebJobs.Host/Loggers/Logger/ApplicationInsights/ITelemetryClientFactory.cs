// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    /// <summary>
    /// Interface for creating <see cref="TelemetryClient"/> instances.
    /// </summary>
    [CLSCompliant(false)]
    public interface ITelemetryClientFactory
    {
        /// <summary>
        /// Creates a <see cref="TelemetryClient"/>.
        /// </summary>        
        /// <param name="instrumentationKey">The Application Insights instrumentation key.</param>
        /// <param name="samplingSettings">The sampling settings, or null to disable sampling.</param>
        /// <returns>The <see cref="TelemetryClient"/>. </returns>
        TelemetryClient Create(string instrumentationKey, SamplingPercentageEstimatorSettings samplingSettings);
    }
}
