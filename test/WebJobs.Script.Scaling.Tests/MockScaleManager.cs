// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class MockScaleManager : ScaleManager
    {
        public MockScaleManager()
            : this(MockBehavior.Default)
        {
        }

        public MockScaleManager(MockBehavior behavior, ScaleSettings settings = null)
            : this(new Mock<MockWorkerInfoProvider>(behavior) { CallBase = behavior != MockBehavior.Strict },
                  new Mock<MockWorkerTable>(behavior) { CallBase = behavior != MockBehavior.Strict },
                  new Mock<MockScaleHandler>(behavior) { CallBase = behavior != MockBehavior.Strict },
                  new Mock<IScaleTracer>(behavior) { CallBase = behavior != MockBehavior.Strict },
                  settings)
        {
        }

        public MockScaleManager(IWorkerInfoProvider provider, IWorkerTable table, IScaleHandler scaleHandler, IScaleTracer tracer, ScaleSettings settings = null)
            : base(provider, table, scaleHandler, tracer, settings ?? ScaleSettings.Instance)
        {
        }

        private MockScaleManager(Mock<MockWorkerInfoProvider> provider, Mock<MockWorkerTable> table, Mock<MockScaleHandler> scaleHandler, Mock<IScaleTracer> tracer, ScaleSettings settings)
            : this(provider.Object, table.Object, scaleHandler.Object, tracer.Object, settings)
        {
            MockWorkerInfoProvider = provider;
            MockWorkerTable = table;
            MockScaleHandler = scaleHandler;
            MockScaleTracer = tracer;
        }

        public Mock<MockWorkerInfoProvider> MockWorkerInfoProvider { get; private set; }

        public Mock<MockWorkerTable> MockWorkerTable { get; private set; }

        public Mock<MockScaleHandler> MockScaleHandler { get; private set; }

        public Mock<IScaleTracer> MockScaleTracer { get; private set; }

        protected override async Task ProcessWorkItem(string activityId)
        {
            await MockProcessWorkItem(activityId);
        }

        public virtual async Task MockProcessWorkItem(string activityId)
        {
            await base.ProcessWorkItem(activityId);
        }

        protected override async Task PingWorker(string activityId, IWorkerInfo worker)
        {
            await MockPingWorker(activityId, worker);
        }

        public virtual async Task MockPingWorker(string activityId, IWorkerInfo worker)
        {
            await base.PingWorker(activityId, worker);
        }

        protected override async Task MakeScaleDecision(string activityId, IWorkerInfo worker)
        {
            await MockMakeScaleDecision(activityId, worker);
        }

        public virtual async Task MockMakeScaleDecision(string activityId, IWorkerInfo worker)
        {
            await base.MakeScaleDecision(activityId, worker);
        }

        protected override async Task CheckStaleWorker(string activityId, IWorkerInfo worker)
        {
            await MockCheckStaleWorker(activityId, worker);
        }

        public virtual async Task MockCheckStaleWorker(string activityId, IWorkerInfo worker)
        {
            await base.CheckStaleWorker(activityId, worker);
        }

        protected override async Task<IWorkerInfo> EnsureManager(string activityId, IWorkerInfo worker)
        {
            return await MockEnsureManager(activityId, worker);
        }

        public virtual async Task<IWorkerInfo> MockEnsureManager(string activityId, IWorkerInfo worker)
        {
            return await base.EnsureManager(activityId, worker);
        }

        protected override async Task<IWorkerInfo> SetManager(string activityId, IWorkerInfo worker, IWorkerInfo current)
        {
            return await MockSetManager(activityId, worker, current);
        }

        public virtual async Task<IWorkerInfo> MockSetManager(string activityId, IWorkerInfo worker, IWorkerInfo current)
        {
            return await base.SetManager(activityId, worker, current);
        }

        protected override async Task<bool> TryRemoveIfMaxWorkers(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await MockTryRemoveIfMaxWorkers(activityId, workers, manager);
        }

        public virtual async Task<bool> MockTryRemoveIfMaxWorkers(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await base.TryRemoveIfMaxWorkers(activityId, workers, manager);
        }

        protected override async Task<bool> TryAddIfLoadFactorMaxWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await MockTryAddIfLoadFactorMaxWorker(activityId, workers, manager);
        }

        public virtual async Task<bool> MockTryAddIfLoadFactorMaxWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await base.TryAddIfLoadFactorMaxWorker(activityId, workers, manager);
        }

        protected override async Task<bool> TrySwapIfLoadFactorMinWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await MockTrySwapIfLoadFactorMinWorker(activityId, workers, manager);
        }

        public virtual async Task<bool> MockTrySwapIfLoadFactorMinWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await base.TrySwapIfLoadFactorMinWorker(activityId, workers, manager);
        }

        protected override async Task<bool> TryAddIfMaxBusyWorkerRatio(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await MockTryAddIfMaxBusyWorkerRatio(activityId, workers, manager);
        }

        public virtual async Task<bool> MockTryAddIfMaxBusyWorkerRatio(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await base.TryAddIfMaxBusyWorkerRatio(activityId, workers, manager);
        }

        protected override async Task<bool> TryRemoveIfMaxFreeWorkerRatio(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await MockTryRemoveIfMaxFreeWorkerRatio(activityId, workers, manager);
        }

        public virtual async Task<bool> MockTryRemoveIfMaxFreeWorkerRatio(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await base.TryRemoveIfMaxFreeWorkerRatio(activityId, workers, manager);
        }

        protected override async Task<bool> TryRemoveSlaveWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await MockTryRemoveSlaveWorker(activityId, workers, manager);
        }

        public virtual async Task<bool> MockTryRemoveSlaveWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager)
        {
            return await base.TryRemoveSlaveWorker(activityId, workers, manager);
        }

        protected override async Task<bool> RequestAddWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager, bool force, bool burst)
        {
            return await MockRequestAddWorker(activityId, workers, manager, force, burst);
        }

        public virtual async Task<bool> MockRequestAddWorker(string activityId, IEnumerable<IWorkerInfo> workers, IWorkerInfo manager, bool force, bool burst)
        {
            return await base.RequestAddWorker(activityId, workers, manager, force, burst);
        }

        protected override async Task<bool> RequestRemoveWorker(string activityId, IWorkerInfo manager, IWorkerInfo toRemove)
        {
            return await MockRequestRemoveWorker(activityId, manager, toRemove);
        }

        public virtual async Task<bool> MockRequestRemoveWorker(string activityId, IWorkerInfo manager, IWorkerInfo toRemove)
        {
            return await base.RequestRemoveWorker(activityId, manager, toRemove);
        }

        public void VerifyAll()
        {
            MockWorkerInfoProvider.VerifyAll();
            MockWorkerTable.VerifyAll();
            MockScaleHandler.VerifyAll();
            MockScaleTracer.VerifyAll();
        }
    }
}