// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Standby
{
    /// <summary>
    /// Triggers PreJIT for compute-separation runtimes during process startup.
    /// </summary>
    /// <remarks>
    /// Only registered when external workers are enabled. In the compute-separation
    /// flow the platform does not call <c>/api/WarmUp</c>, so the PreJIT path in
    /// <see cref="Middleware.HostWarmupMiddleware"/> never fires. This hosted service
    /// runs PreJIT proactively on a background thread before the first
    /// <c>POST /admin/instance/assign</c> arrives. The work is idempotent: if a
    /// warmup HTTP call ever does fire first, <see cref="JitTraceWarmer"/> will
    /// short-circuit subsequent calls.
    /// </remarks>
    internal sealed class ComputeSeparationJitWarmupService : IHostedService
    {
        private readonly JitTraceWarmer _warmer;
        private readonly ILogger<ComputeSeparationJitWarmupService> _logger;

        public ComputeSeparationJitWarmupService(JitTraceWarmer warmer, ILogger<ComputeSeparationJitWarmupService> logger)
        {
            _warmer = warmer ?? throw new ArgumentNullException(nameof(warmer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_warmer.HasRun)
            {
                return Task.CompletedTask;
            }

            _logger.LogInformation("Scheduling background PreJIT for compute-separation.");

            // Run on a background thread so host startup is not blocked. PreJIT
            // walks every entry in the .jittrace file and forces JIT compilation
            // via RuntimeHelpers.PrepareMethod, which can take hundreds of ms.
            _ = Task.Run(() =>
            {
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    _warmer.RunOnce();
                    _logger.LogInformation(
                        "Compute-separation PreJIT completed in {elapsedMilliseconds} ms.",
                        stopwatch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Compute-separation PreJIT failed.");
                }
            }, CancellationToken.None);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
