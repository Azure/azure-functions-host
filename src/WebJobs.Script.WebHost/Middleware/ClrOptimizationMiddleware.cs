// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    /// <summary>
    /// A middleware responsible for Optimizing CLR settings like GC to help with cold start
    /// </summary>
    internal class ClrOptimizationMiddleware
    {
        // This is double the amount of memory allocated during cold start specialization.
        // This value is calculated based on prod profiles across all languages observed for an extended period of time.
        // This value is just a best effort and if for any reason CLR needs to allocate more memory then it will ignore this value.
        private const long AllocationBudgetForGCDuringSpecialization = 16 * 1024 * 1024;
        private readonly ILogger _logger;
        private readonly RequestDelegate _next;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private readonly IEnvironment _environment;
        private RequestDelegate _invoke;
        private double _specialized = 0;

        public ClrOptimizationMiddleware(RequestDelegate next, IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment, ILogger<ClrOptimizationMiddleware> logger)
        {
            _webHostEnvironment = webHostEnvironment;
            _environment = environment;
            _logger = logger;
            _next = next;
            _invoke = _environment.IsAnyLinuxConsumption() ? next : InvokeClrOptimizationCheck;
        }

        public Task Invoke(HttpContext context)
        {
            return _invoke(context);
        }

        private Task InvokeClrOptimizationCheck(HttpContext context)
        {
            var task = _next.Invoke(context).ContinueWith(task =>
            {
                // We are tweaking GC behavior in ClrOptimizationMiddleware as this is one of the last call stacks that get executed during standby mode as well as function exection.
                // We force a GC and enter no GC region in standby mode and exit no GC region after first function execution during specialization.
                StartStopGCAsBestEffort();
            }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            return task;
        }

        private void StartStopGCAsBestEffort()
        {
            try
            {
                // optimization not intended for single core VMs
                if (_webHostEnvironment.InStandbyMode && _environment.GetEffectiveCoresCount() > 1 && !_environment.IsAnyLinuxConsumption())
                {
                    // If in placeholder mode and already in NoGCRegion, let's end it then start NoGCRegion again.
                    // This may happen if there are multiple warmup calls(few minutes apart) during placeholder mode and before specialization.
                    if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
                    {
                        GC.EndNoGCRegion();
                    }

                    // In standby mode, we enter NoGCRegion mode as best effort.
                    // This is to try to avoid GC during cold start specialization.
                    if (!GC.TryStartNoGCRegion(AllocationBudgetForGCDuringSpecialization, disallowFullBlockingGC: false))
                    {
                        _logger.LogError($"CLR runtime GC failed to commit the requested amount of memory: {AllocationBudgetForGCDuringSpecialization}");
                    }
                    _logger.LogInformation($"GC Collection count for gen 0: {GC.CollectionCount(0)}, gen 1: {GC.CollectionCount(1)}, gen 2: {GC.CollectionCount(2)}");
                }
                else
                {
                    // if not in standby mode and we are in NoGCRegion then we end NoGCRegion.
                    if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
                    {
                        GC.EndNoGCRegion();
                        _logger.LogInformation($"GC Collection count for gen 0: {GC.CollectionCount(0)}, gen 1: {GC.CollectionCount(1)}, gen 2: {GC.CollectionCount(2)}");
                    }

                    // This logic needs to run only once during specialization, so replacing the RequestDelegate after specialization
                    if (Interlocked.CompareExchange(ref _specialized, 1, 0) == 0)
                    {
                        Interlocked.Exchange(ref _invoke, _next);
                    }
                }
            }
            catch (Exception ex)
            {
                // Just logging it at informational.
                _logger.LogInformation(ex, "GC optimization will not get applied.");
            }
        }
    }
}
