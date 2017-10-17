// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class WorkerConfigFactoryTests
    {
        [Fact]
        public void SetsDebugPort()
        {
            var lang = "test";
            var extension = ".test";
            var defaultWorkerPath = "./test";

            var workerPath = "/path/to/custom/worker";

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>($"workers:{lang}:debug", "1000"),
                    new KeyValuePair<string, string>($"workers:{lang}:path", workerPath),
                })
                .Build();

            var logger = new TestLogger("test");
            var workerConfigFactory = new WorkerConfigFactory(config, logger);

            var workerConfigs = workerConfigFactory.GetConfigs(new List<IWorkerProvider>()
            {
                new TestWorkerProvider()
                {
                    Language = lang,
                    Extension = extension,
                    DefaultWorkerPath = defaultWorkerPath
                }
            });

            Assert.Equal(workerConfigs.Single().Arguments.WorkerPath, workerPath);
        }
    }
}
