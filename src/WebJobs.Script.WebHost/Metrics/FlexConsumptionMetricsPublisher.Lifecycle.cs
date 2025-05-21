// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Extensions;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public partial class FlexConsumptionMetricsPublisher
    {
        private sealed class Lifecycle : IDisposable
        {
            private readonly FlexConsumptionMetricsPublisher _parent;
            private readonly TimeSpan _initialDelay;
            private readonly TimeSpan _intervalDelay;
            private readonly CancellationTokenSource _cts = new();

            public Lifecycle(FlexConsumptionMetricsPublisher parent, TimeSpan initialDelay, TimeSpan intervalDelay)
            {
                _parent = parent;
                _initialDelay = initialDelay;
                _intervalDelay = intervalDelay;
                PublishLoopAsync(_cts.Token).Forget();
            }

            public void Dispose()
            {
                if (!_cts.IsCancellationRequested)
                {
                    // IsCancellationRequested serves as a dispose check.
                    _cts.Cancel();
                }

                _cts.Dispose();
            }

            private async Task PublishLoopAsync(CancellationToken cancellation)
            {
                try
                {
                    ValueStopwatch stopwatch = ValueStopwatch.StartNew();
                    await Task.Delay(_initialDelay, cancellation);
                    while (!cancellation.IsCancellationRequested)
                    {
                        await _parent.OnPublishMetrics(DateTime.UtcNow, stopwatch);
                        stopwatch = ValueStopwatch.StartNew();
                        await Task.Delay(_intervalDelay, cancellation);
                    }
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    // swallow
                }
            }
        }
    }
}