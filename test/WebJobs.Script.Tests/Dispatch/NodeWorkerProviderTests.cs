using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;
using Microsoft.Azure.WebJobs.Script.Dispatch;
using Microsoft.Extensions.Configuration;
using Microsoft.WebJobs.Script.Tests;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Dispatch
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
