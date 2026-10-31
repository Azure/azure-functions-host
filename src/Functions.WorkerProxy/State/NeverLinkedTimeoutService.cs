// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Owns the never-linked deadline wait for the application lifetime.
/// </summary>
internal sealed class NeverLinkedTimeoutService(WorkerPodStateManager manager) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => manager.WatchNeverLinkedAsync(stoppingToken);
}
