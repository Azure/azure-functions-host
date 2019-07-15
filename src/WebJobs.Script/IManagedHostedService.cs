// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Handles stopping of services that need to happen after StopAsync() of all IHostedService are complete.
    /// </summary>
    public interface IManagedHostedService : IHostedService
    {
        Task OuterStopAsync(CancellationToken cancellationToken);
    }
}