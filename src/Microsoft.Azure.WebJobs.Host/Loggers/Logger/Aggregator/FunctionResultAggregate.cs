// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal class FunctionResultAggregate
    {
        public string Name { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public TimeSpan AverageDuration { get; set; }
        public TimeSpan MaxDuration { get; set; }
        public TimeSpan MinDuration { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public int Count => Successes + Failures;
        public double SuccessRate => Math.Round((Successes / (double)Count) * 100, 2);

        public IReadOnlyDictionary<string, object> ToReadOnlyDictionary()
        {
            return new ReadOnlyDictionary<string, object>(new Dictionary<string, object>
            {
                [LoggingKeys.Name] = Name,
                [LoggingKeys.Count] = Count,
                [LoggingKeys.Timestamp] = Timestamp,
                [LoggingKeys.AvgDuration] = AverageDuration,
                [LoggingKeys.MaxDuration] = MaxDuration,
                [LoggingKeys.MinDuration] = MinDuration,
                [LoggingKeys.Successes] = Successes,
                [LoggingKeys.Failures] = Failures,
                [LoggingKeys.SuccessRate] = SuccessRate
            });
        }
    }
}
