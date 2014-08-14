// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Timers
{
    internal class AlertingRecurrentTaskSeriesCommand : ITaskSeriesCommand
    {
        private readonly IAlertingRecurrentCommand _innerCommand;
        private readonly IDelayStrategy _delayStrategy;

        public AlertingRecurrentTaskSeriesCommand(IAlertingRecurrentCommand innerCommand, IDelayStrategy delayStrategy)
        {
            _innerCommand = innerCommand;
            _delayStrategy = delayStrategy;
        }

        public async Task<TaskSeriesCommandResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            AlertingRecurrentCommandResult result = await _innerCommand.TryExecuteAsync(cancellationToken);
            Task normalDelay = Task.Delay(_delayStrategy.GetNextDelay(result.Succeeded));
            Task wait = Task.WhenAny(normalDelay, result.StopWaiting);
            return new TaskSeriesCommandResult(wait);
        }
    }
}
