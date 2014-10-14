// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class NullHostInstanceLoggerProvider : IHostInstanceLoggerProvider
    {
        Task<IHostInstanceLogger> IHostInstanceLoggerProvider.GetAsync(CancellationToken cancellationToken)
        {
            IHostInstanceLogger logger = new NullHostInstanceLogger();
            return Task.FromResult(logger);
        }

        private class NullHostInstanceLogger : IHostInstanceLogger
        {
            public Task LogHostStartedAsync(HostStartedMessage message, CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }
        }
    }
}
