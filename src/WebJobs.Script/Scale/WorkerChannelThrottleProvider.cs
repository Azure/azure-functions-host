// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    /// <summary>
    /// Throttle provider that monitors the health of OOP worker channels.
    /// </summary>
    public class WorkerChannelThrottleProvider : IConcurrencyThrottleProvider
    {
        private readonly IServiceProvider _serviceProvider;

        public WorkerChannelThrottleProvider(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException(nameof(serviceProvider));
            }

            _serviceProvider = serviceProvider;
        }

        public ConcurrencyThrottleStatus GetStatus(ILogger logger = null)
        {
            var status = new ConcurrencyThrottleStatus
            {
                State = ThrottleState.Disabled
            };

            var dispatcher = _serviceProvider.GetScriptHostServiceOrNull<IFunctionInvocationDispatcher>();
            if (dispatcher != null)
            {
                // TODO: determine channel health based on channel latencies
            }

            return status;
        }
    }
}
