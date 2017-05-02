// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Configuration options for function result aggregation.
    /// </summary>
    public class FunctionResultAggregatorConfiguration
    {
        private int _batchSize;
        private TimeSpan _flushTimeout;

        private const int DefaultBatchSize = 1000;
        private const int MaxBatchSize = 10000;

        private static readonly TimeSpan DefaultFlushTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MaxFlushTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public FunctionResultAggregatorConfiguration()
        {
            _batchSize = DefaultBatchSize;
            _flushTimeout = DefaultFlushTimeout;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the aggregator is enabled.
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating the the maximum batch size for aggregations. When this number is hit, the results are
        /// aggregated and sent to every registered <see cref="ILogger"/>.
        /// </summary>
        public int BatchSize
        {
            get { return _batchSize; }

            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                if (value > MaxBatchSize)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _batchSize = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating when the aggregator will send results to every registered <see cref="ILogger"/>.
        /// </summary>
        public TimeSpan FlushTimeout
        {
            get { return _flushTimeout; }

            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                if (value > MaxFlushTimeout)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _flushTimeout = value;
            }
        }
    }
}
