// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class NodeWorkerProviderTests
    {
        [Fact]
        public void SetsDebugAddress()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>("workers:node:debug", "localhost:2020"),
                })
                .Build();

            var provider = new NodeWorkerProvider();
            var args = new ArgumentsDescription();
            var result = provider.TryConfigureArguments(args, config, new TestLogger("test"));

            Assert.True(result);
            Assert.Contains(args.ExecutableArguments, (exeArgs) => exeArgs.Contains("--inspect=localhost:2020"));
        }

        [Fact]
        public void DisablesDebugIfNotConfigured()
        {
            var config = new ConfigurationBuilder().Build();

            var provider = new NodeWorkerProvider();
            var args = new ArgumentsDescription();
            var result = provider.TryConfigureArguments(args, config, new TestLogger("test"));

            Assert.True(result);
            Assert.DoesNotContain(args.ExecutableArguments, (exeArgs) => exeArgs.Contains("--inspect"));
        }
    }
}
