// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class HeartbeatTimerFactory : ITimerFactory
    {
        private readonly ICanFailCommand _heartbeatCommand;

        public HeartbeatTimerFactory(ICanFailCommand heartbeatCommand)
        {
            _heartbeatCommand = heartbeatCommand;
        }

        public IntervalSeparationTimer Create()
        {
            return LinearSpeedupTimerCommand.CreateTimer(_heartbeatCommand,
                HeartbeatIntervals.NormalSignalInterval, HeartbeatIntervals.MinimumSignalInterval);
        }
    }
}
