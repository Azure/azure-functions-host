// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class NullLinuxContainerActivityPublisher : ILinuxContainerActivityPublisher, IHostedService
    {
        private static readonly Lazy<NullLinuxContainerActivityPublisher> _instance = new Lazy<NullLinuxContainerActivityPublisher>(new NullLinuxContainerActivityPublisher());

        private NullLinuxContainerActivityPublisher()
        {
        }

        public static NullLinuxContainerActivityPublisher Instance => _instance.Value;

        public void PublishFunctionExecutionActivity(ContainerFunctionExecutionActivity activity)
        {
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
