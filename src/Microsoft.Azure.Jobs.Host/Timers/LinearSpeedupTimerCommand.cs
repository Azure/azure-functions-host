using System;

namespace Microsoft.Azure.Jobs
{
    internal class LinearSpeedupTimerCommand : IIntervalSeparationCommand
    {
        private readonly ICanFailCommand _innerCommand;
        private readonly TimeSpan _normalInterval;
        private readonly TimeSpan _minimumInterval;
        private readonly int _failureSpeedupDivisor;

        private TimeSpan _currentInterval;

        public LinearSpeedupTimerCommand(ICanFailCommand innerCommand, TimeSpan normalInterval, TimeSpan minimumInterval)
            : this(innerCommand, normalInterval, minimumInterval, 2)
        {
        }

        public LinearSpeedupTimerCommand(ICanFailCommand innerCommand, TimeSpan normalInterval, TimeSpan minimumInterval,
            int failureSpeedupDivisor)
        {
            if (innerCommand == null)
            {
                throw new ArgumentNullException("innerCommand");
            }

            if (normalInterval.Ticks < 0)
            {
                throw new ArgumentOutOfRangeException("normalInterval", "The TimeSpan must not be negative.");
            }

            if (minimumInterval.Ticks < 0)
            {
                throw new ArgumentOutOfRangeException("minimumInterval", "The TimeSpan must not be negative.");
            }

            if (minimumInterval.Ticks > normalInterval.Ticks)
            {
                throw new ArgumentException("The minimumInterval must not be greater than the normalInterval.",
                    "minimumInterval");
            }

            if (failureSpeedupDivisor < 1)
            {
                throw new ArgumentOutOfRangeException("failureSpeedupDivisor",
                    "The failureSpeedupDivisor must not be less than 1.");
            }

            _innerCommand = innerCommand;
            _normalInterval = normalInterval;
            _minimumInterval = minimumInterval;
            _failureSpeedupDivisor = failureSpeedupDivisor;

            _currentInterval = normalInterval;
        }

        public TimeSpan SeparationInterval
        {
            get { return _currentInterval; }
        }

        public void Execute()
        {
            if (_innerCommand.TryExecute())
            {
                _currentInterval = _normalInterval;
            }
            else
            {
                TimeSpan speedupInterval = new TimeSpan(_currentInterval.Ticks / _failureSpeedupDivisor);
                _currentInterval = Max(speedupInterval, _minimumInterval);
            }
        }

        private static TimeSpan Max(TimeSpan x, TimeSpan y)
        {
            return x.Ticks > y.Ticks ? x : y;
        }

        public static IntervalSeparationTimer CreateTimer(ICanFailCommand command, TimeSpan normalInterval, TimeSpan minimumInterval)
        {
            return new IntervalSeparationTimer(new LinearSpeedupTimerCommand(command, normalInterval, minimumInterval));
        }
    }
}
