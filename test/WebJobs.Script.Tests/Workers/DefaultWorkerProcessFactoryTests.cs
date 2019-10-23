// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.OutOfProc;
using Microsoft.Azure.WebJobs.Script.OutOfProc.Http;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public class DefaultWorkerProcessFactoryTests
    {
        public static IEnumerable<object[]> TestWorkerContexts
        {
            get
            {
                yield return new object[] { new HttpWorkerContext() { Arguments = new WorkerProcessArguments() { ExecutablePath = "test" },  Port = 456, EnvironmentVariables = new Dictionary<string, string>() { { "httpkey1", "httpvalue1" }, { "httpkey2", "httpvalue2" } } } };
                yield return new object[] { new RpcWorkerContext("testId", 500, "testWorkerId", new WorkerProcessArguments() { ExecutablePath = "test" }, "c:\testDir", new Uri("http://localhost")) };
            }
        }

        public static IEnumerable<object[]> InvalidWorkerContexts
        {
            get
            {
                yield return new object[] { new HttpWorkerContext() { Port = 456, EnvironmentVariables = new Dictionary<string, string>() { { "httpkey1", "httpvalue1" }, { "httpkey2", "httpvalue2" } } } };
            }
        }

        [Theory]
        [MemberData(nameof(TestWorkerContexts))]
        public void DefaultWorkerProcessFactory_Returns_ExpectedProcess(WorkerContext workerContext)
        {
            DefaultWorkerProcessFactory defaultWorkerProcessFactory = new DefaultWorkerProcessFactory();
            Process childProcess = defaultWorkerProcessFactory.CreateWorkerProcess(workerContext);

            var expectedEnvVars = workerContext.EnvironmentVariables;
            var actualEnvVars = childProcess.StartInfo.EnvironmentVariables;
            var parentProessEnvVars = Environment.GetEnvironmentVariables();
            Assert.True(expectedEnvVars.Count + parentProessEnvVars.Count == actualEnvVars.Count);
            foreach (var envVar in expectedEnvVars)
            {
                Assert.Equal(expectedEnvVars[envVar.Key], actualEnvVars[envVar.Key]);
            }
            childProcess.Dispose();
        }

        [Theory]
        [MemberData(nameof(InvalidWorkerContexts))]
        public void DefaultWorkerProcessFactory_InvalidWorkerContext_Throws(WorkerContext workerContext)
        {
            DefaultWorkerProcessFactory defaultWorkerProcessFactory = new DefaultWorkerProcessFactory();
            Assert.Throws<ArgumentNullException>(() => defaultWorkerProcessFactory.CreateWorkerProcess(workerContext));
        }

        public IDictionary<string, string> GetTestEnvVars()
        {
            return new Dictionary<string, string>() { { "rpckey1", "rpcvalue1" }, { "rpckey2", "rpcvalue2" } };
        }
    }
}
