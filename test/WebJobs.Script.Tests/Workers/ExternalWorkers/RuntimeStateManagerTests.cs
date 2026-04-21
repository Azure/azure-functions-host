// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.ExternalWorkers
{
    public class RuntimeStateManagerTests
    {
        private readonly RuntimeStateManager _manager = new(NullLogger<RuntimeStateManager>.Instance);

        [Fact]
        public void GetState_Initially_ReportsZeroWorkersAndSlots()
        {
            var state = _manager.GetState();

            Assert.Equal(20, state.MaxLinkedWorkers);
            Assert.Equal(0, state.LinkedWorkerCount);
            Assert.Equal(0, state.TotalRequestSlots);
            Assert.Equal(0, state.TotalAvailableRequestSlots);
        }

        [Fact]
        public void OnWorkerLinked_IncrementsLinkedCountWithoutCapacity()
        {
            int changes = 0;
            _manager.StateChanged += () => changes++;

            _manager.OnWorkerLinked("w1");

            var state = _manager.GetState();
            Assert.Equal(1, state.LinkedWorkerCount);
            Assert.Equal(0, state.TotalRequestSlots);
            Assert.Equal(0, state.TotalAvailableRequestSlots);
            Assert.Equal(1, changes);
        }

        [Fact]
        public void OnWorkerLinked_Duplicate_IsIdempotent()
        {
            _manager.OnWorkerLinked("w1");

            int changes = 0;
            _manager.StateChanged += () => changes++;

            _manager.OnWorkerLinked("w1");

            var state = _manager.GetState();
            Assert.Equal(1, state.LinkedWorkerCount);
            Assert.Equal(0, changes);
        }

        [Fact]
        public void OnWorkerLinked_NullOrEmptyId_Throws()
        {
            Assert.Throws<ArgumentException>(() => _manager.OnWorkerLinked(null));
            Assert.Throws<ArgumentException>(() => _manager.OnWorkerLinked(string.Empty));
        }

        [Fact]
        public void OnWorkerUnlinked_DecrementsLinkedCount()
        {
            _manager.OnWorkerLinked("w1");
            _manager.OnWorkerLinked("w2");

            _manager.OnWorkerUnlinked("w1");

            var state = _manager.GetState();
            Assert.Equal(1, state.LinkedWorkerCount);
        }

        [Fact]
        public void OnWorkerUnlinked_UnknownWorker_IsNoOp()
        {
            _manager.OnWorkerLinked("w1");

            int changes = 0;
            _manager.StateChanged += () => changes++;

            _manager.OnWorkerUnlinked("does-not-exist");

            var state = _manager.GetState();
            Assert.Equal(1, state.LinkedWorkerCount);
            Assert.Equal(0, changes);
        }

        [Fact]
        public void OnWorkerCapacityAvailable_AddsToTotalSlots()
        {
            _manager.OnWorkerLinked("w1");

            _manager.OnWorkerCapacityAvailable("w1", 16);

            var state = _manager.GetState();
            Assert.Equal(1, state.LinkedWorkerCount);
            Assert.Equal(16, state.TotalRequestSlots);
            Assert.Equal(16, state.TotalAvailableRequestSlots);
        }

        [Fact]
        public void OnWorkerCapacityAvailable_Duplicate_IsIdempotent()
        {
            _manager.OnWorkerCapacityAvailable("w1", 16);

            int changes = 0;
            _manager.StateChanged += () => changes++;

            _manager.OnWorkerCapacityAvailable("w1", 99);

            var state = _manager.GetState();
            Assert.Equal(16, state.TotalRequestSlots);
            Assert.Equal(0, changes);
        }

        [Fact]
        public void OnWorkerCapacityAvailable_HeterogeneousCapacities_Sum()
        {
            _manager.OnWorkerCapacityAvailable("w1", 16);
            _manager.OnWorkerCapacityAvailable("w2", 4);

            var state = _manager.GetState();
            Assert.Equal(20, state.TotalRequestSlots);
        }

        [Fact]
        public void OnWorkerCapacityAvailable_NonPositiveCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.OnWorkerCapacityAvailable("w1", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.OnWorkerCapacityAvailable("w1", -1));
        }

        [Fact]
        public void OnWorkerCapacityUnavailable_SubtractsOriginalCapacity()
        {
            _manager.OnWorkerCapacityAvailable("w1", 16);
            _manager.OnWorkerCapacityAvailable("w2", 4);

            _manager.OnWorkerCapacityUnavailable("w1");

            var state = _manager.GetState();
            Assert.Equal(4, state.TotalRequestSlots);
        }

        [Fact]
        public void OnWorkerCapacityUnavailable_UnknownWorker_IsNoOp()
        {
            _manager.OnWorkerCapacityAvailable("w1", 16);

            int changes = 0;
            _manager.StateChanged += () => changes++;

            _manager.OnWorkerCapacityUnavailable("does-not-exist");

            var state = _manager.GetState();
            Assert.Equal(16, state.TotalRequestSlots);
            Assert.Equal(0, changes);
        }

        [Fact]
        public void DrainingWorker_StaysLinkedButSurrendersCapacity()
        {
            _manager.OnWorkerLinked("w1");
            _manager.OnWorkerCapacityAvailable("w1", 16);

            _manager.OnWorkerCapacityUnavailable("w1");

            var state = _manager.GetState();
            Assert.Equal(1, state.LinkedWorkerCount);
            Assert.Equal(0, state.TotalRequestSlots);
            Assert.Equal(0, state.TotalAvailableRequestSlots);
        }

        [Fact]
        public void AcquireSlots_WhenSufficient_GrantsFullRequest()
        {
            _manager.OnWorkerCapacityAvailable("w1", 16);

            int granted = _manager.AcquireSlots(5);

            var state = _manager.GetState();
            Assert.Equal(5, granted);
            Assert.Equal(16, state.TotalRequestSlots);
            Assert.Equal(11, state.TotalAvailableRequestSlots);
        }

        [Fact]
        public void AcquireSlots_WhenInsufficient_GrantsPartial()
        {
            _manager.OnWorkerCapacityAvailable("w1", 4);

            int granted = _manager.AcquireSlots(10);

            var state = _manager.GetState();
            Assert.Equal(4, granted);
            Assert.Equal(0, state.TotalAvailableRequestSlots);
        }

        [Fact]
        public void AcquireSlots_WhenNoneAvailable_GrantsZeroWithoutStateChange()
        {
            int changes = 0;
            _manager.StateChanged += () => changes++;

            int granted = _manager.AcquireSlots(5);

            Assert.Equal(0, granted);
            Assert.Equal(0, changes);
        }

        [Fact]
        public void AcquireSlots_NonPositiveCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.AcquireSlots(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.AcquireSlots(-1));
        }

        [Fact]
        public void ReleaseSlots_ReturnsSlotsToPool()
        {
            _manager.OnWorkerCapacityAvailable("w1", 16);
            _manager.AcquireSlots(5);

            _manager.ReleaseSlots(3);

            var state = _manager.GetState();
            Assert.Equal(14, state.TotalAvailableRequestSlots);
        }

        [Fact]
        public void ReleaseSlots_OverRelease_ClampsAtZero()
        {
            _manager.OnWorkerCapacityAvailable("w1", 16);
            _manager.AcquireSlots(2);

            _manager.ReleaseSlots(10);

            var state = _manager.GetState();
            Assert.Equal(16, state.TotalAvailableRequestSlots);
        }

        [Fact]
        public void ReleaseSlots_NonPositiveCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.ReleaseSlots(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _manager.ReleaseSlots(-1));
        }

        [Fact]
        public void ReleaseSlots_NothingLeased_DoesNotRaiseStateChanged()
        {
            // Mirrors AcquireSlots: a no-op release (over-released to zero) must
            // not trigger a debounced publish to the mesh service.
            _manager.OnWorkerCapacityAvailable("w1", 16);

            int changes = 0;
            _manager.StateChanged += () => changes++;

            _manager.ReleaseSlots(5);

            Assert.Equal(0, changes);
        }

        [Fact]
        public void GetState_WhenLeasedExceedsTotal_ClampsAvailableAtZero()
        {
            _manager.OnWorkerCapacityAvailable("w1", 16);
            _manager.AcquireSlots(10);
            _manager.OnWorkerCapacityUnavailable("w1");

            var state = _manager.GetState();
            Assert.Equal(0, state.TotalRequestSlots);
            Assert.Equal(0, state.TotalAvailableRequestSlots);
        }

        [Fact]
        public void StateChanged_FiresOnEveryMutation()
        {
            int changes = 0;
            _manager.StateChanged += () => changes++;

            _manager.OnWorkerLinked("w1");
            _manager.OnWorkerCapacityAvailable("w1", 16);
            _manager.AcquireSlots(2);
            _manager.ReleaseSlots(1);
            _manager.OnWorkerCapacityUnavailable("w1");
            _manager.OnWorkerUnlinked("w1");

            Assert.Equal(6, changes);
        }

        [Fact]
        public void SetStopping_ClampsTotalAndAvailableSlotsToZero()
        {
            _manager.OnWorkerLinked("w1");
            _manager.OnWorkerCapacityAvailable("w1", 16);
            _manager.AcquireSlots(4);

            _manager.SetStopping();

            var state = _manager.GetState();
            Assert.Equal(1, state.LinkedWorkerCount);
            Assert.Equal(0, state.TotalRequestSlots);
            Assert.Equal(0, state.TotalAvailableRequestSlots);
        }

        [Fact]
        public void SetStopping_AcquireSlots_GrantsZeroWithoutStateChange()
        {
            _manager.OnWorkerCapacityAvailable("w1", 16);
            _manager.SetStopping();

            int changes = 0;
            _manager.StateChanged += () => changes++;

            int granted = _manager.AcquireSlots(5);

            Assert.Equal(0, granted);
            Assert.Equal(0, changes);
        }

        [Fact]
        public void SetStopping_RaisesStateChangedOnce()
        {
            int changes = 0;
            _manager.StateChanged += () => changes++;

            _manager.SetStopping();
            _manager.SetStopping();
            _manager.SetStopping();

            Assert.Equal(1, changes);
        }
    }
}
