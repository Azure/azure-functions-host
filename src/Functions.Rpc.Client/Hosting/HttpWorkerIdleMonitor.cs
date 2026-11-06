// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Rpc.Client;

internal sealed class HttpWorkerIdleMonitor(IWorkerChannelRegistry registry, TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(ScanInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await registry.RetireIdleHttpWorkersAsync(IdleThreshold, stoppingToken);
        }
    }
}
