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
        public void SetsDebugPort()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>("workers:node:debug", "2020"),
                })
                .Build();

            var provider = new NodeWorkerProvider();
            var args = new ArgumentsDescription();
            var result = provider.TryConfigureArguments(args, config, new TestLogger("test"));

            Assert.True(result);
            Assert.Contains(args.ExecutableArguments, (exeArgs) => exeArgs.Contains("--inspect=2020"));
        }
    }
}
