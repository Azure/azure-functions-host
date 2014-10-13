// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class ExpectInstanceFailureTaskFunctionInstanceLogger : IFunctionInstanceLogger
    {
        private readonly TaskCompletionSource<Exception> _taskSource;

        public ExpectInstanceFailureTaskFunctionInstanceLogger(TaskCompletionSource<Exception> taskSource)
        {
            _taskSource = taskSource;
        }

        public Task<string> LogFunctionStartedAsync(FunctionStartedMessage message, CancellationToken cancellationToken)
        {
            return Task.FromResult(String.Empty);
        }

        public Task LogFunctionCompletedAsync(FunctionCompletedMessage message, CancellationToken cancellationToken)
        {
            if (message != null)
            {
                // This class is used when a function is expected to fail (the result of the task is the expected
                // exception).
                // A faulted task is reserved for unexpected failures (like unhandled background exceptions).
                _taskSource.SetResult(message.Failure != null ? message.Failure.Exception : null);
            }

            return Task.FromResult(0);
        }

        public Task DeleteLogFunctionStartedAsync(string startedMessageId, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
