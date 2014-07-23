// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class NullHostInstanceLogger : IHostInstanceLogger
    {
        public Task LogHostStartedAsync(HostStartedMessage message, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
