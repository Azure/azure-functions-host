// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace WebJobs.Script.WebHost.Diagnostics
{
    // Log to ETW.
    public class MetricsFastLogger : FastLogger
    {
        public MetricsFastLogger(string accountConnectionString)
            : base(accountConnectionString)
        {
        }

        public override Task AddAsync(FunctionInstanceLogEntry item, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!item.EndTime.HasValue)
            {
                MetricsEventManager.FunctionStarted();
            }
            else
            {
                MetricsEventManager.FunctionCompleted();
            }

            return base.AddAsync(item, cancellationToken);
        }
    }    
}