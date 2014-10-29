// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class ExpectManualCompletionFunctionInstanceLogger<TResult> : IFunctionInstanceLogger
    {
        private readonly TaskCompletionSource<TResult> _taskSource;
        private readonly HashSet<string> _ignoreFailureFunctions;

        public ExpectManualCompletionFunctionInstanceLogger(TaskCompletionSource<TResult> taskSource,
            IEnumerable<string> ignoreFailureFunctions)
        {
            _taskSource = taskSource;
            _ignoreFailureFunctions = ignoreFailureFunctions != null ?
                new HashSet<string>(ignoreFailureFunctions) : new HashSet<string>();
        }

        public Task<string> LogFunctionStartedAsync(FunctionStartedMessage message, CancellationToken cancellationToken)
        {
            return Task.FromResult(String.Empty);
        }

        public Task LogFunctionCompletedAsync(FunctionCompletedMessage message, CancellationToken cancellationToken)
        {
            if (message != null && message.Failure != null && message.Function != null &&
                !_ignoreFailureFunctions.Contains(message.Function.FullName))
            {
                _taskSource.SetException(message.Failure.Exception);
            }

            return Task.FromResult(0);
        }

        public Task DeleteLogFunctionStartedAsync(string startedMessageId, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
