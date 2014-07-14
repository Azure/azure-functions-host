using System;

namespace Microsoft.Azure.Jobs.Host.Timers
{
    internal class ExponentialBackoffTimerCommand : IIntervalSeparationCommand
    {
        private readonly ICanFailCommand _innerCommand;
        private readonly TimeSpan _minimumInterval;
        private readonly TimeSpan _maximumInterval;

        private TimeSpan _currentInterval;
        private uint _backoffExponent;

        public ExponentialBackoffTimerCommand(ICanFailCommand innerCommand, TimeSpan minimumInterval,
            TimeSpan maximumInterval)
        {
            if (innerCommand == null)
            {
                throw new ArgumentNullException("innerCommand");
            }

            if (minimumInterval.Ticks < 0)
            {
                throw new ArgumentOutOfRangeException("minimumInterval", "The TimeSpan must not be negative.");
            }

            if (maximumInterval.Ticks < 0)
            {
                throw new ArgumentOutOfRangeException("maximumInterval", "The TimeSpan must not be negative.");
            }

            if (minimumInterval.Ticks > maximumInterval.Ticks)
            {
                throw new ArgumentException("The minimumInterval must not be greater than the maximumInterval.",
                    "minimumInterval");
            }

            _innerCommand = innerCommand;
            _minimumInterval = minimumInterval;
            _maximumInterval = maximumInterval;

            _currentInterval = TimeSpan.Zero; // Don't delay initial execution
        }

        public TimeSpan SeparationInterval
        {
            get { return _currentInterval; }
        }

        public void Execute()
        {
            if (_innerCommand.TryExecute())
            {
                _currentInterval = _minimumInterval;
                _backoffExponent = 0;
            }
            else
            {
                TimeSpan backoffInterval = new TimeSpan(_minimumInterval.Ticks * (long)Math.Pow(2, _backoffExponent));

                if (backoffInterval.Ticks < _maximumInterval.Ticks)
                {
                    _currentInterval = backoffInterval;
                    _backoffExponent++;
                }
                else
                {
                    _currentInterval = _maximumInterval;
                }
            }
        }

        public static IntervalSeparationTimer CreateTimer(ICanFailCommand command, TimeSpan minimumInterval,
            TimeSpan maximumInterval)
        {
            return new IntervalSeparationTimer(new ExponentialBackoffTimerCommand(command, minimumInterval,
                maximumInterval));
        }
    }
}
