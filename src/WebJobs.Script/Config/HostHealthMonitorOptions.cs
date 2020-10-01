// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class HostHealthMonitorOptions : IOptionsFormatter
    {
        internal const float DefaultCounterThreshold = 0.80F;

        public HostHealthMonitorOptions()
        {
            Enabled = true;

            // these default numbers translate to a 50% health rating
            // over the window.
            HealthCheckInterval = TimeSpan.FromSeconds(10);
            HealthCheckWindow = TimeSpan.FromMinutes(2);
            HealthCheckThreshold = 6;
            CounterThreshold = DefaultCounterThreshold;
        }

        /// <summary>
        /// Gets or sets a value indicating whether host health monitoring
        /// is enabled.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the interval at which host health will be checked
        /// for threshold overages.
        /// </summary>
        public TimeSpan HealthCheckInterval { get; set; }

        /// <summary>
        /// Gets or sets a value defining the sliding time window
        /// that <see cref="HealthCheckThreshold"/> applies to.
        /// </summary>
        public TimeSpan HealthCheckWindow { get; set; }

        /// <summary>
        /// Gets or sets the threshold for the <see cref="HealthCheckWindow"/>.
        /// When the host has been unhealthy a number of times exceeding this
        /// threshold, the host app domain will be recycled in an attempt to recover.
        /// </summary>
        public int HealthCheckThreshold { get; set; }

        /// <summary>
        /// Gets or sets the counter threshold for all counters.
        /// </summary>
        public float CounterThreshold { get; set; }

        public string Format()
        {
            var options = new JObject
            {
                { nameof(Enabled), Enabled },
                { nameof(HealthCheckInterval), HealthCheckInterval },
                { nameof(HealthCheckWindow), HealthCheckWindow },
                { nameof(CounterThreshold), CounterThreshold }
            };

            return options.ToString(Formatting.Indented);
        }
    }
}