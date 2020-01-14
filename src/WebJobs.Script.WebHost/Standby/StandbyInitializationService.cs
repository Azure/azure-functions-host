// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class StandbyInitializationService : IHostedService
    {
        private readonly IStandbyManager _standbyManager;

        public StandbyInitializationService(IStandbyManager standbyManager)
        {
            _standbyManager = standbyManager ?? throw new ArgumentNullException(nameof(standbyManager));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _standbyManager.InitializeAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
