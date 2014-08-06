// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class HeartbeatFunctionExecutor : IFunctionExecutor
    {
        private readonly ICanFailCommand _heartbeatCommand;
        private readonly IFunctionExecutor _innerExecutor;

        public HeartbeatFunctionExecutor(ICanFailCommand heartbeatCommand, IFunctionExecutor innerExecutor)
        {
            _heartbeatCommand = heartbeatCommand;
            _innerExecutor = innerExecutor;
        }

        public async Task<IDelayedException> TryExecuteAsync(IFunctionInstance instance,
            CancellationToken cancellationToken)
        {
            IDelayedException result;

            using (IntervalSeparationTimer timer = CreateHeartbeatTimer())
            {

                await _heartbeatCommand.TryExecuteAsync(cancellationToken);
                timer.Start();

                result = await _innerExecutor.TryExecuteAsync(instance, cancellationToken);

                timer.Stop();
            }

            return result;
        }

        private IntervalSeparationTimer CreateHeartbeatTimer()
        {
            return LinearSpeedupTimerCommand.CreateTimer(_heartbeatCommand,
                HeartbeatIntervals.NormalSignalInterval, HeartbeatIntervals.MinimumSignalInterval);
        }
    }
}
