// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    // Based on: https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/Extensions/ValueStopwatch/ValueStopwatch.cs
    public struct ValueStopwatch
    {
        private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

        // Note: *not* readonly to prevent a defensive copy
        private long _startTimestamp;

        private ValueStopwatch(long startTimestamp)
        {
            _startTimestamp = startTimestamp;
        }

        public bool IsActive => _startTimestamp != 0;

        public static ValueStopwatch StartNew() => new ValueStopwatch(Stopwatch.GetTimestamp());

        public TimeSpan GetElapsedTime()
        {
            // Start timestamp can't be zero in an initialized ValueStopwatch. It would have to be literally the first thing executed when the machine boots to be 0.
            // So it being 0 is a clear indication of default(ValueStopwatch)
            if (!IsActive)
            {
                throw new InvalidOperationException("An uninitialized, or 'default', ValueStopwatch cannot be used to get elapsed time.");
            }

            var end = Stopwatch.GetTimestamp();
            var timestampDelta = end - _startTimestamp;
            var ticks = (long)(TimestampToTicks * timestampDelta);
            return new TimeSpan(ticks);
        }
    }
}