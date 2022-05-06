// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class StandbyInitializationService : IHostedService
    {
        private readonly IStandbyManager _standbyManager;
        private readonly ILogger _logger;

        public StandbyInitializationService(IStandbyManager standbyManager, ILogger logger)
        {
            _standbyManager = standbyManager ?? throw new ArgumentNullException(nameof(standbyManager));
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                return _standbyManager.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting StandbyInitialization service. Handling error and continuing.");
                return Task.CompletedTask;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
