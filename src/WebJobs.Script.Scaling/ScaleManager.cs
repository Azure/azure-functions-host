// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    public class ScaleManager : IDisposable
    {
        private readonly IWorkerInfoProvider _provider;
        private readonly IWorkerTable _table;
        private readonly IScaleHandler _eventHandler;
        private readonly IScaleTracer _tracer;
        private readonly ScaleSettings _settings;

        private IWorkerInfo _worker;
        private Timer _workerUpdateTimer;
        private bool _disposed;
        private bool _pingResult;

        private DateTime _pingWorkerUtc = DateTime.MinValue;
        private DateTime _managerCheckUtc = DateTime.MinValue;
        private DateTime _staleWorkerCheckUtc = DateTime.MinValue;
        private DateTime _scaleCheckUtc = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScaleManager"/> class.
        /// core function runtime will instantiate this with table and event handler implementation
        /// </summary>
        public ScaleManager(IWorkerInfoProvider provider, IWorkerTable table, IScaleHandler eventHandler, IScaleTracer tracer)
            : this(provider, table, eventHandler, tracer, ScaleSettings.Instance)
        {
        }

        public ScaleManager(IWorkerInfoProvider provider, IWorkerTable table, IScaleHandler eventHandler, IScaleTracer tracer, ScaleSettings settings)
        {
            _provider = provider;
            _table = table;
            _eventHandler = eventHandler;
            _tracer = tracer;
            _settings = settings;

            _workerUpdateTimer = new Timer(OnUpdateWorkerStatus, null, Timeout.Infinite, Timeout.Infinite);
        }

        ~ScaleManager()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Start()
        {
            // start one right away
            _workerUpdateTimer?.Change(0, Timeout.Infinite);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    var timer = _workerUpdateTimer;
                    _workerUpdateTimer = null;

                    if (timer != null)
                    {
                        using (var evt = new ManualResetEvent(false))
                        {
                            timer.Dispose(evt);
                            evt.WaitOne();
                        }
                    }
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// this is timer callback every ScaleUtils.WorkerUpdateInterval.
        /// </summary>
        private void OnUpdateWorkerStatus(object state)
        {
            var activityId = Guid.NewGuid().ToString();
            try
            {
                ProcessWorkItem(activityId).Wait();
            }
            catch (Exception ex)
            {
                if (_worker != null)
                {
                    _tracer.TraceError(activityId, _worker, string.Format("OnUpdateWorkerStatus failed with {0}", ex.GetBaseException()));
                }
            }
            finally
            {
                var workerUpdateTimer = _workerUpdateTimer;
                if (workerUpdateTimer != null)
                {
                    try
                    {
                        workerUpdateTimer.Change((int)_settings.WorkerUpdateInterval.TotalMilliseconds, Timeout.Infinite);
                    }
                    catch (ObjectDisposedException)
                    {
                        // expected if racy with dispose
                    }
                }
            }
        }

        /// <summary>
        /// this routine does ..
        /// - ping and update worker status
        /// - ensure manager
        /// - if manager, make scale decision
        /// - if manager, stale worker management
        /// </summary>
        protected virtual async Task ProcessWorkItem(string activityId)
        {
            // get worker status for this worker
            _worker = await _provider.GetWorkerInfo(activityId);

            // update status for this worker and keep alive
            await PingWorker(activityId, _worker);

            // select manager
            var manager = await EnsureManager(activityId, _worker);

            // if this worker is the manager, perform scale decision and stale worker management
            if (ScaleUtils.WorkerEquals(_worker, manager))
            {
                // perform scale decision
                await MakeScaleDecision(activityId, _worker);

                // stale worker management
                await CheckStaleWorker(activityId, _worker);
            }
        }

        /// <summary>
        /// Add worker to table and ping to keep alive
        /// </summary>
        protected virtual async Task PingWorker(string activityId, IWorkerInfo worker)
        {
            // if ping was unsuccessful, keep pinging.  this is to address
            // the issue where site continue to run on an unassigned worker.
            if (!_pingResult || _pingWorkerUtc < DateTime.UtcNow)
            {
                // if PingWorker throws, we will not update the worker status
                // this worker will be stale and eventually removed.
                _pingResult = await _eventHandler.PingWorker(activityId, worker);

                _pingWorkerUtc = DateTime.UtcNow.Add(_settings.WorkerPingInterval);
            }

            // check if worker is valid for the site
            if (_pingResult)
            {
                await _table.AddOrUpdate(worker);

                _tracer.TraceUpdateWorker(activityId, worker, $"Current worker load factor {worker.LoadFactor}");
            }
            else
            {
                _tracer.TraceWarning(activityId, worker, string.Format("Worker does not belong to the site."));

                await _table.Delete(worker);

                _tracer.TraceRemoveWorker(activityId, worker, "Worker removed");

                throw new InvalidOperationException("The worker does not belong to the site.");
            }
        }

        /// <summary>
        /// this routine ensure a manager
        /// - check if existing active manager
        /// - if yes, check (prefer homestamp) manager
        /// - if no, become (prefer homestamp) manager
        /// </summary>
        protected virtual async Task<IWorkerInfo> EnsureManager(string activityId, IWorkerInfo worker)
        {
            var manager = await _table.GetManager();

            if (DateTime.UtcNow < _managerCheckUtc)
            {
                return manager;
            }

            try
            {
                // no manager or current one stale
                if (manager == null || manager.IsStale)
                {
                    // if this worker is homestamp or no homestamp worker exists
                    if (worker.IsHomeStamp)
                    {
                        return await SetManager(activityId, worker, manager);
                    }
                    else
                    {
                        var workers = await _table.ListNonStale();
                        if (!workers.Any(w => w.IsHomeStamp))
                        {
                            return await SetManager(activityId, worker, manager);
                        }
                    }
                }
                else if (!manager.IsHomeStamp)
                {
                    // prefer home stamp
                    if (worker.IsHomeStamp)
                    {
                        return await SetManager(activityId, worker, manager);
                    }
                }

                return manager;
            }
            finally
            {
                _managerCheckUtc = DateTime.UtcNow.Add(_settings.ManagerCheckInterval);
            }
        }

        /// <summary>
        /// nominate itself to be a manager
        /// </summary>
        protected virtual async Task<IWorkerInfo> SetManager(string activityId, IWorkerInfo worker, IWorkerInfo current)
        {
            var tableLock = await _table.AcquireLock();
            _tracer.TraceInformation(activityId, worker, string.Format("Acquire table lock id: {0}", tableLock.Id));
            try
            {
                var manager = await _table.GetManager();

                // other worker already takes up manager position
                if (!ScaleUtils.WorkerEquals(manager, current))
                {
                    return manager;
                }

                await _table.SetManager(worker);

                _tracer.TraceInformation(activityId, worker, "This worker is set to be a manager.");

                return worker;
            }
            finally
            {
                await tableLock.Release();
                _tracer.TraceInformation(activityId, worker, string.Format("Release table lock id: {0}", tableLock.Id));
            }
        }

        /// <summary>
        /// this routine checks stale worker performed by manager
        /// if ping request failed, we will request remove that worker
        /// if ping request Worker Not Found, we will remove from the table
        /// </summary>
        protected virtual async Task CheckStaleWorker(string activityId, IWorkerInfo manager)
        {
            const int CheckStaleBatch = 5;

            if (DateTime.UtcNow < _staleWorkerCheckUtc)
            {
                return;
            }

            try
            {
                var stales = await _table.ListStale();
                _tracer.TraceInformation(activityId, manager, stales.GetSummary("Stale"));

                await Task.WhenAll(stales.Take(CheckStaleBatch).Select(async stale =>
                {
                    Exception exception = null;
                    bool validWorker = false;
                    try
                    {
                        validWorker = await _eventHandler.PingWorker(activityId, stale);
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }

                    // worker failed to respond
                    if (exception != null)
                    {
                        _tracer.TraceWarning(activityId, stale, string.Format("Stale worker (LastModifiedTimeUtc={0}) failed ping request {1}", stale.LastModifiedTimeUtc, exception));

                        await RequestRemoveWorker(activityId, manager, stale);
                    }
                    else if (!validWorker)
                    {
                        _tracer.TraceWarning(activityId, stale, string.Format("Stale worker (LastModifiedTimeUtc={0}) does not belong to the site.", stale.LastModifiedTimeUtc));

                        await _table.Delete(stale);

                        _tracer.TraceRemoveWorker(activityId, stale, string.Format("Stale worker (LastModifiedTimeUtc={0}) removed by manager ({1}).", stale.LastModifiedTimeUtc, manager.ToDisplayString()));
                    }
                    else
                    {
                        _tracer.TraceInformation(activityId, stale, string.Format("Stale worker (LastModifiedTimeUtc={0}) ping successfully by manager ({1}).", stale.LastModifiedTimeUtc, manager.ToDisplayString()));
                    }
                }));
            }
            catch (Exception ex)
            {
                _tracer.TraceError(activityId, manager, string.Format("CheckStaleWorker failed with {0}", ex));
            }
            finally
            {
                _staleWorkerCheckUtc = DateTime.UtcNow.Add(_settings.StaleWorkerCheckInterval);
            }
        }

        /// <summary>
        /// this routine makes scaling decision performed by manager
        /// - remove if exceeds MaxWorkers
        /// - add any int.MaxValue
        /// - remove any int.MinValue
        /// - add if busy workers exceeds MaxBusyWorkerRatio
        /// - remove if free workers exceeds MaxFreeWorkerRatio
        /// - shrink back to homestamp if no busy worker
        /// </summary>
        protected virtual async Task MakeScaleDecision(string activityId, IWorkerInfo manager)
        {
            if (DateTime.UtcNow < _scaleCheckUtc)
            {
                return;
            }

            try
            {
                // Get the current set of active workers and make a scale decision
                var workers = await _table.ListNonStale();
                var workerStatus = workers.GetSummary("NonStale");
                _tracer.TraceInformation(activityId, manager, workerStatus);

                // We only perform a single scale operation per interval - i.e. we
                // stop at the first successful operation
                if (await TryRemoveIfMaxWorkers(activityId, workers, manager))
                {
                    return;
                }

                if (await TryAddIfLoadFactorMaxWorker(activityId, workers, manager))
                {
                    return;
                }

                if (await TrySwapIfLoadFactorMinWorker(activityId, workers, manager))
                {
                    return;
                }

                if (await TryAddIfMaxBusyWorkerRatio(activityId, workers, manager))
                {
                    return;
                }

                if (await TryRemoveIfMaxFreeWorkerRatio(activityId, workers, manager))
                {
                    return;
                }

                if (await TryRemoveSlaveWorker(activityId, workers, manager))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                _tracer.TraceError(activityId, manager, string.Format("MakeScaleDecision failed: {0}", ex));
            }
            finally
            {
                _scaleCheckUtc = DateTime.UtcNow.Add(_settings.ScaleCheckInterval);
            }
        }

        protected virtual async Task<bool> TryRemoveIfMaxWorkers(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            var workersCount = workers.Count();
            var maxWorkers = _settings.MaxWorkers;

            if (workersCount > maxWorkers)
            {
                _tracer.TraceInformation(activityId, manager, $"Number of workers ({workersCount}) exceeds maximum number of workers ({maxWorkers}) allowed.");

                var workersToRemove = workers.SortByRemovingOrder().Take(workersCount - maxWorkers).ToArray();
                var removeTasks = workersToRemove.Select(async toRemove =>
                {
                    await RequestRemoveWorker(activityId, manager, toRemove);
                });
                await Task.WhenAll(removeTasks);

                // we return true if any requests were made
                // regardless of whether they succeeded
                return true;
            }

            return false;
        }

        protected virtual async Task<bool> TryAddIfLoadFactorMaxWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            var loadFactorMaxWorker = workers.FirstOrDefault(w => w.LoadFactor == int.MaxValue);
            if (loadFactorMaxWorker != null)
            {
                _tracer.TraceInformation(activityId, loadFactorMaxWorker, "Worker have int.MaxValue loadfactor.");

                await RequestAddWorker(activityId, workers, manager);

                // we return true if any requests were made
                // regardless of whether they succeeded
                return true;
            }

            return false;
        }

        protected virtual async Task<bool> TrySwapIfLoadFactorMinWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            // only swap one worker at a time
            var loadFactorMinWorker = workers.SortByRemovingOrder().FirstOrDefault(w => w.LoadFactor == int.MinValue);
            if (loadFactorMinWorker != null)
            {
                _tracer.TraceInformation(activityId, loadFactorMinWorker, "Worker has int.MinValue loadfactor.");

                // swap the workers, force add then remove regardless whether add succeeded
                await RequestAddWorker(activityId, workers, manager, force: true);

                await RequestRemoveWorker(activityId, manager, loadFactorMinWorker);

                // we return true if any requests were made
                // regardless of whether they succeeded
                return true;
            }

            return false;
        }

        protected virtual async Task<bool> TryAddIfMaxBusyWorkerRatio(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            // If the number of busy workers exceeds the threshold, we'll make a scale out request
            int workerCount = workers.Count();
            var busyWorkers = workers.Where(w => w.LoadFactor >= _settings.BusyWorkerLoadFactor);
            var busyWorkerRatio = (busyWorkers.Count() * 1.0) / workerCount;
            if (busyWorkerRatio > _settings.MaxBusyWorkerRatio)
            {
                _tracer.TraceInformation(activityId, manager, string.Format("Busy worker ratio ({0:0.000}) exceeds maximum busy worker ratio ({1:0.000}).", busyWorkerRatio, _settings.MaxBusyWorkerRatio));

                // if we're at 0 or 1 workers and we need to add workers,
                // we'll "burst" add
                bool burst = false; // workerCount <= 1;

                await RequestAddWorker(activityId, workers, manager, burst: burst);

                // we return true if any requests were made
                // regardless of whether they succeeded
                return true;
            }

            return false;
        }

        protected virtual async Task<bool> TryRemoveIfMaxFreeWorkerRatio(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            // If the number of non-busy/free workers exceeds the threshold, we'll make a scale down request
            var freeWorkers = workers.Where(w => w.LoadFactor <= _settings.FreeWorkerLoadFactor);
            var freeWorkerRatio = (freeWorkers.Count() * 1.0) / workers.Count();
            if (freeWorkerRatio > _settings.MaxFreeWorkerRatio)
            {
                _tracer.TraceInformation(activityId, manager, string.Format("Free worker ratio ({0:0.000}) exceeds maximum free worker ratio ({1:0.000}).", freeWorkerRatio, _settings.MaxFreeWorkerRatio));

                var toRemove = workers.SortByRemovingOrder().FirstOrDefault();
                if (toRemove != null)
                {
                    await RequestRemoveWorker(activityId, manager, toRemove);
                }

                // we return true if any requests were made
                // regardless of whether they succeeded
                return true;
            }

            return false;
        }

        protected virtual async Task<bool> TryRemoveSlaveWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            // if no busy worker
            if (!workers.Any(w => w.LoadFactor >= _settings.BusyWorkerLoadFactor))
            {
                var toRemove = workers.SortByRemovingOrder().FirstOrDefault(w => !w.IsHomeStamp);
                if (toRemove != null)
                {
                    _tracer.TraceInformation(activityId, toRemove, string.Format("Try remove slave worker to scale back to home stamp by manager ({0}).", manager.ToDisplayString()));

                    // Add worker to home stamp
                    if (await RequestAddWorker(activityId, Enumerable.Empty<IWorkerInfo>(), manager, force: true))
                    {
                        // only when new worker added, slave worker will be removed
                        await RequestRemoveWorker(activityId, manager, toRemove);
                    }

                    // we return true if any requests were made
                    // regardless of whether they succeeded
                    return true;
                }
            }

            return false;
        }

        protected virtual async Task<bool> RequestAddWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager, bool force = false, bool burst = false)
        {
            int currentWorkerCount = workers.Count();
            if (!force && currentWorkerCount >= _settings.MaxWorkers)
            {
                _tracer.TraceWarning(activityId, manager, string.Format("Unable to add new worker due to maximum number of workers ({0}) allowed.", _settings.MaxWorkers));
                return false;
            }
            else
            {
                // determine the number of workers to add
                // when adding in "burst" mode, we'll add several
                int numWorkersToAdd = burst ? 3 : 1;

                // try to add a worker
                var stampNames = workers.GroupBy(w => w.StampName).Select(g => g.Key);
                var stampName = await _eventHandler.TryAddWorker(activityId, stampNames, numWorkersToAdd);
                if (!string.IsNullOrEmpty(stampName))
                {
                    _tracer.TraceAddWorker(activityId, manager, $"{numWorkersToAdd} worker(s) added to stamp {stampName}");
                    return true;
                }
                else
                {
                    _tracer.TraceWarning(activityId, manager, $"Unable to add worker to existing {stampNames.ToDisplayString()} stamps.");
                    return false;
                }
            }
        }

        protected virtual async Task<bool> RequestRemoveWorker(string activityId, IWorkerInfo manager, IWorkerInfo toRemove)
        {
            if (await _eventHandler.TryRemoveWorker(activityId, toRemove))
            {
                await _table.Delete(toRemove);

                _tracer.TraceRemoveWorker(activityId, toRemove, $"Worker removed by manager ({manager.ToDisplayString()})");

                return true;
            }

            return false;
        }
    }
}