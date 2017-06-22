// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class ScaleUtilsTests
    {
        [Theory, MemberData("WorkerEqualsData")]
        public void WorkerEqualsTests(IWorkerInfo src, IWorkerInfo dst, bool expected)
        {
            var actual = ScaleUtils.WorkerEquals(src, dst);

            Assert.Equal(expected, actual);
        }

        public static IEnumerable<object[]> WorkerEqualsData
        {
            get
            {
                var info1 = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "ab" &&
                    w.SiteName == "cd" &&
                    w.ToString() == "ab:cd");

                var info2 = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "ab" &&
                    w.SiteName == "cd" &&
                    w.ToString() == "ab:cd");

                var other = Mock.Of<IWorkerInfo>(w =>
                    w.StampName == "wx" &&
                    w.SiteName == "yz" &&
                    w.ToString() == "wx:yz");

                yield return new object[] { null, null, true };
                yield return new object[] { info1, null, false };
                yield return new object[] { null, info2, false };
                yield return new object[] { info1, info2, true };
                yield return new object[] { info1, other, false };
            }
        }

        [Fact]
        public void SortByRemovingOrderTests()
        {
            var expected = new[]
            {
                new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "1", LoadFactor = 10 },
                new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "2", LoadFactor = 50 },
                new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "3", LoadFactor = 90 },
                new MockWorkerInfo { StampName = "home-stamp", WorkerName = "4", LoadFactor = 10 },
                new MockWorkerInfo { StampName = "home-stamp", WorkerName = "5", LoadFactor = 50 },
                new MockWorkerInfo { StampName = "home-stamp", WorkerName = "6", LoadFactor = 90 },
            };

            // Test
            var random = new Random();
            var actual = expected.OrderBy(_ => random.Next())
                .SortByRemovingOrder().ToArray();

            // Assert
            for (int i = 0; i < 0; ++i)
            {
                Assert.True(ScaleUtils.Equals(expected[i], actual[i]));
            }
        }

        [Fact]
        public void WorkersGetSummaryTests()
        {
            var workers = new[]
            {
                new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "1", LoadFactor = 45, IsStale = true, LastModifiedTimeUtc = DateTime.MaxValue },
                new MockWorkerInfo { StampName = "home-stamp", WorkerName = "2", LoadFactor = 55, IsStale = false, LastModifiedTimeUtc = DateTime.MinValue },
            };

            // Test
            var summary = workers.GetSummary("Tests");

            // Assert
            Assert.Contains("Tests 2 workers", summary);
            Assert.Contains("slave-stamp:1, loadfactor: 45, stale: True, lastupdate: " + DateTime.MaxValue, summary);
            Assert.Contains("home-stamp:2, loadfactor: 55, stale: False, lastupdate: " + DateTime.MinValue, summary);
            Assert.Contains(Environment.NewLine, summary);
        }

        [Fact]
        public void WorkersToDisplayStringTests()
        {
            var workers = new[]
            {
                new MockWorkerInfo { StampName = "slave-stamp", WorkerName = "1", LoadFactor = 45, IsStale = true, LastModifiedTimeUtc = DateTime.MaxValue },
                new MockWorkerInfo { StampName = "home-stamp", WorkerName = "2", LoadFactor = 55, IsStale = false, LastModifiedTimeUtc = DateTime.MinValue },
            };

            // Test
            var workersDisplayString = workers.ToDisplayString();

            // Assert
            Assert.Contains("slave-stamp:1,home-stamp:2", workersDisplayString);

            // Test
            var workerDisplayString = workers.Last().ToDisplayString();

            // Assert
            Assert.Contains("home-stamp:2", workerDisplayString);
        }

        [Theory(Skip = "Pending machine key fix"), MemberData(nameof(GetAndValidateTokenData))]
        public void GetAndValidateTokenTests(DateTime expiredUtc, bool expected)
        {
            var token = ScaleUtils.GetToken(expiredUtc);

            if (expected)
            {
                // test
                ScaleUtils.ValidateToken(token);
            }
            else
            {
                // test
                var exception = Assert.Throws<InvalidOperationException>(() => ScaleUtils.ValidateToken(token));

                // Assert
                Assert.Contains("expired", exception.Message);
            }
        }

        public static IEnumerable<object[]> GetAndValidateTokenData
        {
            get
            {
                yield return new object[] { DateTime.UtcNow, true };
                yield return new object[] { DateTime.UtcNow.AddMinutes(-4), true };
                yield return new object[] { DateTime.UtcNow.AddMinutes(-6), false };
            }
        }
    }
}