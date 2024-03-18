// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
#nullable enable

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    /// <summary>
    /// Defines the methods that are required for a host metrics provider.
    /// The provider is can be used to collects metrics from the
    /// HostMetrics meter and makes them available as a HostMetricsPayload object.
    /// </summary>
    public interface IHostMetricsProvider
    {
        /// <summary>
        /// Gets the instance ID.
        /// </summary>
        public string InstanceId { get; }

        /// <summary>
        /// Gets the name of the function group for this instance.
        /// </summary>
        public string FunctionGroup { get; }

        /// <summary>
        /// Initializes the provider and starts collecting metrics.
        /// </summary>
        public void Start();

        /// <summary>
        /// Retrieves a dictionary of available metrics, or null.
        /// </summary>
        public IReadOnlyDictionary<string, long>? GetHostMetricsOrNull();

        /// <summary>
        /// Determines whether the provider has any host metrics.
        /// </summary>
        public bool HasMetrics();
    }
}
