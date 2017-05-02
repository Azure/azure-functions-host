// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ApplicationInsights;

namespace Microsoft.Azure.WebJobs.Logging.ApplicationInsights
{
    /// <summary>
    /// Interface for creating <see cref="TelemetryClient"/> instances.
    /// </summary>
    public interface ITelemetryClientFactory : IDisposable
    {
        /// <summary>
        /// Creates a <see cref="TelemetryClient"/>.
        /// </summary>
        /// <returns>The <see cref="TelemetryClient"/>. </returns>
        TelemetryClient Create();
    }
}
