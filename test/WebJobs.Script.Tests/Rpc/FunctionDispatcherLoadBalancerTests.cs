// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class FunctionDispatcherLoadBalancerTests
    {
        [Fact]
        public static void LoadBalancer_EqualDistribution_EvenNumberOfChannels()
        {
            List<ILanguageWorkerChannel> results = new List<ILanguageWorkerChannel>();
            int totalInvocations = 100;

            IEnumerable<ILanguageWorkerChannel> workerChannels = new List<ILanguageWorkerChannel>()
            {
                new TestLanguageWorkerChannel("1"),
                new TestLanguageWorkerChannel("2"),
                new TestLanguageWorkerChannel("3"),
                new TestLanguageWorkerChannel("4")
            };

            int expectedInvocationsPerChannel = totalInvocations / workerChannels.Count();

            IFunctionDispatcherLoadBalancer loadBalancer = new FunctionDispatcherLoadBalancer();
            for (int index = 0; index < 100; index++)
            {
                results.Add(loadBalancer.GetLanguageWorkerChannel(workerChannels, workerChannels.Count()));
            }
            var channelGroupsQuery = results.GroupBy(r => r.Id)
            .Select(g => new { Value = g.Key, Count = g.Count() });

            foreach (var channelGroup in channelGroupsQuery)
            {
                Assert.True(channelGroup.Count == expectedInvocationsPerChannel);
            }
        }

        [Fact]
        public static void LoadBalancer_OddNumberOfChannels()
        {
            List<ILanguageWorkerChannel> results = new List<ILanguageWorkerChannel>();
            int totalInvocations = 100;

            IEnumerable<ILanguageWorkerChannel> workerChannels = new List<ILanguageWorkerChannel>()
            {
                new TestLanguageWorkerChannel("1"),
                new TestLanguageWorkerChannel("2"),
                new TestLanguageWorkerChannel("3")
            };

            int expectedInvocationsPerChannel = totalInvocations / workerChannels.Count();

            IFunctionDispatcherLoadBalancer loadBalancer = new FunctionDispatcherLoadBalancer();
            for (int index = 0; index < 100; index++)
            {
                results.Add(loadBalancer.GetLanguageWorkerChannel(workerChannels, workerChannels.Count()));
            }
            var channelGroupsQuery = results.GroupBy(r => r.Id)
            .Select(g => new { Value = g.Key, Count = g.Count() });

            foreach (var channelGroup in channelGroupsQuery)
            {
                Assert.True(channelGroup.Count >= expectedInvocationsPerChannel);
            }
        }

        [Fact]
        public static void LoadBalancer_SingleProcess_VerifyCounter()
        {
            List<ILanguageWorkerChannel> results = new List<ILanguageWorkerChannel>();

            IEnumerable<ILanguageWorkerChannel> workerChannels = new List<ILanguageWorkerChannel>()
            {
                new TestLanguageWorkerChannel("1"),
            };

            IFunctionDispatcherLoadBalancer loadBalancer = new FunctionDispatcherLoadBalancer();
            FunctionDispatcherLoadBalancer functionDispatcherLoadBalancer = loadBalancer as FunctionDispatcherLoadBalancer;
            for (int index = 0; index < 10; index++)
            {
                loadBalancer.GetLanguageWorkerChannel(workerChannels, 1);
                Assert.Equal(0, functionDispatcherLoadBalancer.Counter);
            }
        }

        [Fact]
        public static void LoadBalancer_Throws_InvalidOperationException_NoWorkerChannels()
        {
            List<ILanguageWorkerChannel> results = new List<ILanguageWorkerChannel>();
            IEnumerable<ILanguageWorkerChannel> workerChannels = new List<ILanguageWorkerChannel>();
            IFunctionDispatcherLoadBalancer loadBalancer = new FunctionDispatcherLoadBalancer();

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                loadBalancer.GetLanguageWorkerChannel(workerChannels, workerChannels.Count());
            });

            Assert.Equal($"Did not find any initialized language workers", ex.Message);
        }
    }
}
