// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    internal sealed class NullHostedService : IHostedService
    {
        private static readonly Lazy<NullHostedService> _instance = new Lazy<NullHostedService>(() => new NullHostedService());

        public static NullHostedService Instance => _instance.Value;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
