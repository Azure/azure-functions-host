﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public class DefaultWorkerProcessFactoryTests
    {
        public static IEnumerable<object[]> TestWorkerContexts
        {
            get
            {
                yield return new object[]
                {
                    new HttpWorkerContext()
                    {
                        Arguments = new WorkerProcessArguments()
                        {
                            ExecutablePath = "test",
                            ExecutableArguments = new List<string>() { "%httpkey1%", "%TestEnv%" },
                            WorkerArguments = new List<string>() { "%httpkey2%" }
                        },
                        Port = 456,
                        EnvironmentVariables = new Dictionary<string, string>() { { "httpkey1", "httpvalue1" }, { "httpkey2", "httpvalue2" } }
                    }
                };
                yield return new object[]
                {
                    new RpcWorkerContext(
                    "testId",
                    500,
                    "testWorkerId",
                    new WorkerProcessArguments()
                    {
                        ExecutablePath = "test",
                        ExecutableArguments = new List<string>() { "%httpkey1%", "%TestEnv%" },
                        WorkerArguments = new List<string>() { "%httpkey2%" }
                    },
                    "c:\testDir",
                    new Uri("http://localhost"),
                    new Dictionary<string, string>() { { "httpkey1", "httpvalue1" }, { "httpkey2", "httpvalue2" } })
                };
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
            Environment.SetEnvironmentVariable("TestEnv", "TestVal");
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
            if (workerContext is RpcWorkerContext)
            {
                Assert.Equal(" httpvalue1 TestVal httpvalue2 --host localhost --port 80 --workerId testWorkerId --requestId testId --grpcMaxMessageLength 2147483647", childProcess.StartInfo.Arguments);
            }
            else
            {
                Assert.Equal(" httpvalue1 TestVal httpvalue2", childProcess.StartInfo.Arguments);
            }
            childProcess.Dispose();
            Environment.SetEnvironmentVariable("TestEnv", string.Empty);
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
