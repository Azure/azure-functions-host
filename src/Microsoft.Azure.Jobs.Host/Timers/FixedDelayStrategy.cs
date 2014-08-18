// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Timers
{
    internal class FixedDelayStrategy : IDelayStrategy
    {
        private readonly TimeSpan _delayInterval;

        public FixedDelayStrategy(TimeSpan delayInterval)
        {
            if (delayInterval.Ticks < 0)
            {
                throw new ArgumentOutOfRangeException("delayInterval", "The TimeSpan must not be negative.");
            }

            _delayInterval = delayInterval;
        }

        public TimeSpan GetNextDelay(bool executionSucceeded)
        {
            return _delayInterval;
        }

        public static ITaskSeriesTimer CreateTimer(IRecurrentCommand command, TimeSpan initialInterval,
            TimeSpan delayInterval)
        {
            IDelayStrategy delayStrategy = new FixedDelayStrategy(delayInterval);
            ITaskSeriesCommand timerCommand = new RecurrentTaskSeriesCommand(command, delayStrategy);
            return new TaskSeriesTimer(timerCommand, Task.Delay(initialInterval));
        }
    }
}
