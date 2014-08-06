// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Timers
{
    internal class ExponentialBackoffTimerCommand : IIntervalSeparationCommand
    {
        public const double RandomizationFactor = 0.2;

        private readonly ICanFailCommand _innerCommand;
        private readonly TimeSpan _minimumInterval;
        private readonly TimeSpan _maximumInterval;
        private readonly TimeSpan _deltaBackoff;

        private TimeSpan _currentInterval;
        private uint _backoffExponent;
        private Random _random;

        public ExponentialBackoffTimerCommand(ICanFailCommand innerCommand, TimeSpan minimumInterval,
            TimeSpan maximumInterval, TimeSpan deltaBackoff)
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
            _deltaBackoff = deltaBackoff;

            _currentInterval = TimeSpan.Zero; // Don't delay initial execution
        }

        public TimeSpan SeparationInterval
        {
            get { return _currentInterval; }
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            bool succeeded = await _innerCommand.TryExecuteAsync(cancellationToken);

            if (succeeded)
            {
                _currentInterval = _minimumInterval;
                _backoffExponent = 1;
            }
            else
            {
                TimeSpan backoffInterval = _minimumInterval;

                if (_backoffExponent > 0)
                {
                    if (_random == null)
                    {
                        _random = new Random();
                    }

                    double incrementMsec = _random.Next(1.0 - RandomizationFactor, 1.0 + RandomizationFactor) * 
                        Math.Pow(2.0, _backoffExponent - 1) * 
                        _deltaBackoff.TotalMilliseconds;
                    backoffInterval += TimeSpan.FromMilliseconds(incrementMsec);
                }

                if (backoffInterval < _maximumInterval)
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
            return CreateTimer(command, minimumInterval, maximumInterval, minimumInterval);
        }

        public static IntervalSeparationTimer CreateTimer(ICanFailCommand command, TimeSpan minimumInterval,
            TimeSpan maximumInterval, TimeSpan deltaBackoff)
        {
            return new IntervalSeparationTimer(new ExponentialBackoffTimerCommand(command, minimumInterval,
                maximumInterval, deltaBackoff));
        }
    }
}
