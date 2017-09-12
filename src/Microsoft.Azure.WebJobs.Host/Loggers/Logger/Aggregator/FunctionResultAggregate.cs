// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Microsoft.Azure.WebJobs.Logging
{
    internal class FunctionResultAggregate
    {
        public string Name { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public TimeSpan MaxDuration { get; set; }
        public TimeSpan MinDuration { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }

        public IReadOnlyDictionary<string, object> ToReadOnlyDictionary()
        {
            return new ReadOnlyDictionary<string, object>(new Dictionary<string, object>
            {
                [LogConstants.NameKey] = Name,
                [LogConstants.TimestampKey] = Timestamp,
                [LogConstants.TotalDurationKey] = TotalDuration.TotalMilliseconds,
                [LogConstants.MaxDurationKey] = MaxDuration.TotalMilliseconds,
                [LogConstants.MinDurationKey] = MinDuration.TotalMilliseconds,
                [LogConstants.SuccessesKey] = Successes,
                [LogConstants.FailuresKey] = Failures,
            });
        }
    }
}
