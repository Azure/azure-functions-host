// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class WorkerConcurrencyOptions
    {
        public WorkerConcurrencyOptions()
        {
            // Setting default values
            LatencyThreshold = TimeSpan.FromMilliseconds(100);
            AdjustmentPeriod = TimeSpan.FromSeconds(20);
            CheckInterval = TimeSpan.FromSeconds(10);
            HistorySize = 6;
            NewWorkerThreshold = 0.3F;
            MaxWorkerCount = 10;
        }

        /// <summary>
        /// Gets or sets the latency threshold dictating when worker channel is overloaded.
        /// </summary>
        public TimeSpan LatencyThreshold { get; set; }

        /// <summary>
        /// Gets or sets the time when worker
        /// channels will start to monitor again after adding a new worker.
        /// </summary>
        public TimeSpan AdjustmentPeriod { get; set; }

        /// <summary>
        /// Gets or sets interval to check worker channel state.
        /// </summary>
        public TimeSpan CheckInterval { get; set; }

        /// <summary>
        /// Gets or sets the history size to store workers channel states.
        /// </summary>
        public int HistorySize { get; set; }

        /// <summary>
        /// Gets or sets the threshold dictating when a new worker will be added.
        /// Value should be between 0 and 1 indicating the percentage of overloaded channel latency samples required to trigger a addition of a new worker
        /// </summary>
        [Range(typeof(float), "0F", "1F", ErrorMessage = "Value for {0} must be between {1} and {2}.")]
        public float NewWorkerThreshold { get; set; }

        /// <summary>
        /// Gets or sets the max count of workers.
        /// It will be set depending on SKU if equal to 0.
        /// </summary>
        [Range(typeof(int), "1", "100", ErrorMessage = "Value for {0} must be between {1} and {2}.")]
        public int MaxWorkerCount { get; set; }
    }
}
