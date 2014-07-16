// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.Jobs.Host.Timers
{
    internal class FixedIntervalsTimerCommand : IIntervalSeparationCommand
    {
        private readonly ICanFailCommand _innerCommand;
        private readonly TimeSpan _normalInterval;

        private TimeSpan _currentInterval;

        public FixedIntervalsTimerCommand(ICanFailCommand innerCommand, TimeSpan initialInterval, TimeSpan normalInterval)
        {
            if (innerCommand == null)
            {
                throw new ArgumentNullException("innerCommand");
            }

            if (initialInterval.Ticks < 0)
            {
                throw new ArgumentOutOfRangeException("initialInterval", "The TimeSpan must not be negative.");
            }

            if (normalInterval.Ticks < 0)
            {
                throw new ArgumentOutOfRangeException("normalInterval", "The TimeSpan must not be negative.");
            }

            _innerCommand = innerCommand;
            _normalInterval = normalInterval;

            _currentInterval = initialInterval;
        }

        public TimeSpan SeparationInterval
        {
            get { return _currentInterval; }
        }

        public void Execute()
        {
            // Ignore return value;
            _innerCommand.TryExecute();
            _currentInterval = _normalInterval;
        }

        public static IntervalSeparationTimer CreateTimer(ICanFailCommand command, TimeSpan initialInterval,
            TimeSpan normalInterval)
        {
            return new IntervalSeparationTimer(
                new FixedIntervalsTimerCommand(command, initialInterval, normalInterval));
        }
    }
}
