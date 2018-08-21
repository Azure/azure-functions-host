// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class StandbyInitializationService : IHostedService
    {
        private readonly IOptions<ScriptApplicationHostOptions> _applicationOptions;
        private readonly ILoggerFactory _loggerFactory;

        public StandbyInitializationService(IOptions<ScriptApplicationHostOptions> applicationOptions, ILoggerFactory loggerFactory)
        {
            _applicationOptions = applicationOptions ?? throw new ArgumentNullException(nameof(applicationOptions));
            _loggerFactory = loggerFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ILogger logger = _loggerFactory.CreateLogger(LogCategories.Startup);
            await StandbyManager.InitializeAsync(_applicationOptions.Value, logger);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
