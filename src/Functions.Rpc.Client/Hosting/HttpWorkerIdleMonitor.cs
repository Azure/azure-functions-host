// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Rpc.Client;

internal sealed class HttpWorkerIdleMonitor(IWorkerChannelRegistry registry, TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await registry.RetireIdleHttpWorkersAsync(TimeSpan.FromMinutes(2), stoppingToken);
        }
    }
}
