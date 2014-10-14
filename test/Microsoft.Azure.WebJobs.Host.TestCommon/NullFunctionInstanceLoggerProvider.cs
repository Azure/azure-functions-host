// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class NullFunctionInstanceLoggerProvider : IFunctionInstanceLoggerProvider
    {
        Task<IFunctionInstanceLogger> IFunctionInstanceLoggerProvider.GetAsync(CancellationToken cancellationToken)
        {
            IFunctionInstanceLogger logger = new NullFunctionInstanceLogger();
            return Task.FromResult(logger);
        }

        private class NullFunctionInstanceLogger : IFunctionInstanceLogger
        {
            public Task<string> LogFunctionStartedAsync(FunctionStartedMessage message,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(string.Empty);
            }

            public Task LogFunctionCompletedAsync(FunctionCompletedMessage message, CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }

            public Task DeleteLogFunctionStartedAsync(string startedMessageId, CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }
        }
    }
}
