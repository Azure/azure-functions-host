// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class NullLinuxContainerActivityPublisher : ILinuxContainerActivityPublisher, IHostedService
    {
        public NullLinuxContainerActivityPublisher(ILogger<NullLinuxContainerActivityPublisher> logger)
        {
            var nullLogger = logger ?? throw new ArgumentNullException(nameof(logger));
            nullLogger.LogDebug($"Initializing {nameof(NullLinuxContainerActivityPublisher)}");
        }

        public void PublishFunctionExecutionActivity(ContainerFunctionExecutionActivity activity)
        {
             //do nothing
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
