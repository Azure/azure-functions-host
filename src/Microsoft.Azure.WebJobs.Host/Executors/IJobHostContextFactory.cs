// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal interface IJobHostContextFactory
    {
        Task<JobHostContext> CreateAndLogHostStartedAsync(JobHost host, CancellationToken shutdownToken, CancellationToken cancellationToken);
    }
}
