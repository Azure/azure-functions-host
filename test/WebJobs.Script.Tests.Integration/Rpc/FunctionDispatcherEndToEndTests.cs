﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionDispatcherEndToEndTests : IClassFixture<FunctionDispatcherEndToEndTests.TestFixture>
    {
        private LanguageWorkerChannel _nodeWorkerChannel;

        public FunctionDispatcherEndToEndTests(TestFixture fixture)
        {
            Fixture = fixture;
        }

        public TestFixture Fixture { get; set; }

        [Fact]
        public async Task InitializeWorkers_Retries_Succeed()
        {
            await WaitForJobHostChannelReady();
            KillProcess(_nodeWorkerChannel.WorkerProcess.Id);
            await WaitForWorkerProcessRestart(0);

            KillProcess(_nodeWorkerChannel.WorkerProcess.Id);
            await WaitForWorkerProcessRestart(1);
        }

        private async Task WaitForWorkerProcessRestart(int restartCount)
        {
            await TestHelpers.Await(() =>
            {
                var currentChannel = GetCurrentJobHostWorkerChannel();
                return currentChannel != null && currentChannel.Id != _nodeWorkerChannel.Id;

            }, pollingInterval: 4 * 1000, timeout: 60 * 1000);
            _nodeWorkerChannel = GetCurrentJobHostWorkerChannel();
        }

        private async Task WaitForJobHostChannelReady()
        {
            await TestHelpers.Await(() =>
            {
                var currentChannel = GetCurrentJobHostWorkerChannel();
                return currentChannel != null;
            }, pollingInterval: 4 * 1000, timeout: 60 * 1000);
            _nodeWorkerChannel = GetCurrentJobHostWorkerChannel();
        }

        private static void KillProcess(int oldProcId)
        {
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/C taskkill /pid {oldProcId} /f";
            process.StartInfo = startInfo;
            process.Start();
        }

        private LanguageWorkerChannel GetCurrentJobHostWorkerChannel()
        {
            FunctionDispatcher fd = Fixture.JobHost.FunctionDispatcher as FunctionDispatcher;
            return (LanguageWorkerChannel)fd.JobHostLanguageWorkerChannelManager.GetChannels().FirstOrDefault();
        }

        public class TestFixture : ScriptHostEndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Node", "node", LanguageWorkerConstants.NodeLanguageWorkerName,
                startHost: true, functions: new[] { "HttpTrigger" })
            {
            }
        }
    }
}