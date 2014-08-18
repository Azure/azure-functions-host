// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Timers
{
    internal class NullRecurrentCommand : IRecurrentCommand
    {
        public Task<bool> TryExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }
}
