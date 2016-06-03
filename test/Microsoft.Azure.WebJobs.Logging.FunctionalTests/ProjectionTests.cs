// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Xunit;

namespace Microsoft.Azure.WebJobs.Logging.Internal.FunctionalTests
{
    // Test projection from raw logging rows to linear buckets that can be graphed. 
    public class ProjectionTests
    {
        private static readonly int durationMs = 10;
        private static readonly long durationTicks = TimeSpan.FromMilliseconds(durationMs).Ticks;

        private static readonly long t0 = 100 * 1000;
        private static readonly long t1 = t0 + durationTicks;
        private static readonly long t2 = t1 + durationTicks;
        private static readonly long t3 = t2 + durationTicks;
        private static readonly long t4 = t3 + durationTicks;
        private static readonly long t5 = t4 + durationTicks;

        private static Tuple<long, double>[] Work(InstanceCountEntity[] rows, long startTicks, long endTicks, int numberBuckets)
        {
            return ProjectionHelper.Work(rows, startTicks, endTicks, numberBuckets);
        }

        private static InstanceCountEntity[] GetData()
        {
            var rows = new InstanceCountEntity[] {
                new InstanceCountEntity(t1, "c1") { DurationMilliseconds = durationMs, CurrentActive = 6, MachineSize = 20 },
                new InstanceCountEntity(t2, "c1") { DurationMilliseconds = durationMs, CurrentActive = 6, MachineSize = 20 },
            };
            return rows;
        }

        private static readonly int StdArea = 6 * 20; // CurrentActive & MachineSize

        [Fact]
        public void TestScale()
        {
            var rows = GetData();

            double totalArea = 0;
            foreach (var row in rows)
            {
                totalArea += row.GetDurationInTicks() * row.CurrentActive * row.MachineSize;
            }

            // Scale # of buckets shouldn't matter. Always has the same area
            for (int i = 1; i < 20; i++)
            {
                var fx = Work(rows, t0, t5, i);
                var sx = Sum(fx, t0, t5);
                var round = Math.Round(sx, MidpointRounding.AwayFromZero);
                Assert.Equal(round, totalArea);
            }
        }

        // Test 1:1 relationship between source data and projection
        [Fact]
        public void Basic()
        {
            var rows = GetData();

            var f1 = Work(rows, t1, t3, 2); // 1:1
            Assert.Equal(2, f1.Length);
            Assert.Equal(StdArea, f1[0].Item2);
            Assert.Equal(StdArea, f1[1].Item2);
        }

        // Test multiple rows projecting into a single bucket at hte same Scale
        [Fact]
        public void SingleBucket_Same()
        {
            var rows = GetData();

            var f2 = Work(rows, t1, t3, 1); // single bucket , same
            Assert.Equal(1, f2.Length);
            Assert.Equal(StdArea, f2[0].Item2);
        }

        // Project into a longer bucket. Value is half since bucket is twice as wide. 
        [Fact]
        public void SingleBucket_Long()
        {
            var rows = GetData();

            var f2b = Work(rows, t1, t5, 1); // long bucket , half
            Assert.Equal(1, f2b.Length);
            Assert.Equal(StdArea/2, f2b[0].Item2);
        }

        // Project into more granular buckets. Proportionately distribute
        [Fact]
        public void MoreGranularBuckets()
        {
            var rows = GetData();

            var f3 = Work(rows, t1, t3, 4); 
            Assert.Equal(4, f3.Length);
            Assert.Equal(StdArea, f3[0].Item2);
            Assert.Equal(StdArea, f3[1].Item2);
            Assert.Equal(StdArea, f3[2].Item2);
            Assert.Equal(StdArea, f3[3].Item2);
        }

        // Project into more granular buckets. Proportionately distribute
        [Fact]
        public void SubsectionLeft()
        {
            var rows = GetData();

            var f4 = Work(rows, t1, t2, 1); // subsection 
            Assert.Equal(1, f4.Length);
            Assert.Equal(t1, f4[0].Item1);
            Assert.Equal(StdArea, f4[0].Item2);
        }

        // Project into more granular buckets. Proportionately distribute
        [Fact]
        public void SubsectionRight()
        {
            var rows = GetData();

            var f5 = Work(rows, t2, t3, 1); // subsection 
            Assert.Equal(1, f5.Length);
            Assert.Equal(t2, f5[0].Item1);
            Assert.Equal(StdArea, f5[0].Item2);
        }

        // Projection does not fall into bucket range; so buckets are 0.
        [Fact]
        public void OutsideLeft()
        {
            var rows = GetData();

            // Totally outside should be 0. 
            var f6 = Work(rows, t0, t1, 2); // empty 
            Assert.Equal(2, f6.Length);
            Assert.Equal(t0, f6[0].Item1);
            Assert.Equal(0, f6[0].Item2);
            Assert.Equal((t0 + t1) / 2, f6[1].Item1);
            Assert.Equal(0, f6[1].Item2);
        }

        // Projection does not fall into bucket range; so buckets are 0.
        [Fact]
        public void OutsideRight()
        {
            var rows = GetData();

            var f6b = Work(rows, t3, t4, 2); // empty 
            Assert.Equal(2, f6b.Length);
            Assert.Equal(t3, f6b[0].Item1);
            Assert.Equal(0, f6b[0].Item2);
            Assert.Equal((t3 + t4) / 2, f6b[1].Item1);
            Assert.Equal(0, f6b[1].Item2);
        }

        // Projection does not fall into bucket range; so buckets are 0.
        [Fact]
        public void Single_odd_bucket()
        {
            var rows = GetData();

            // Projection in single bucket            
            var f7 = Work(rows, t0, t5, 1); // larger bucket.  smaller. 
            Assert.Equal(1, f7.Length);
            Assert.Equal(48, f7[0].Item2);
        }

        // projection across multiple odd buckets
        [Fact]
        public void multiple_odd_buckets()
        {
            var rows = GetData();

            var f8 = Work(rows, t0, t5, 2); // split buckets. 
            Assert.Equal(2, f8.Length);
            Assert.Equal(96, f8[0].Item2);
            Assert.Equal((t2+t3)/2, f8[1].Item1);
            Assert.Equal(0, f8[1].Item2);
        }

        // Area should be preserved.
        static double Sum(Tuple<long, double>[] results, long startTicks, long endTicks)
        {
            int buckets = results.Length;
            double bucketWidth = ((double) endTicks - startTicks) / buckets;

            double total = 0;
            foreach (var tuple in results)
            {
                var value = tuple.Item2;
                total += (value * bucketWidth);
            }
            return total;
        }
    }
}
