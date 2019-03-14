// Copyright (c) .NET Foundation. All rights reserved.
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
        private LanguageWorkerState _channelState;
        private LanguageWorkerChannel _nodeWorkerChannel;
        private ILanguageWorkerProcess _nodeWorkerProcess;
        private string _functionName = "HttpTrigger";

        public FunctionDispatcherEndToEndTests(TestFixture fixture)
        {
            Fixture = fixture;
            _channelState = Fixture.JobHost.FunctionDispatcher.LanguageWorkerChannelState;
            _nodeWorkerChannel = GetCurrentWorkerChannel();
            _nodeWorkerProcess = GetCurrentWorkerProcess();
        }

        public TestFixture Fixture { get; set; }

        [Fact]
        public async Task InitializeWorkers_Fails_AddsFunctionErrors()
        {

            for (int i = 0; i < 3; i++)
            {
                KillProcess(_nodeWorkerProcess.WorkerProcess.Id);
                await WaitForWorkerProcessRestart(i);
            }

            ICollection<string> actualFunctionErrors = Fixture.JobHost.FunctionErrors[_functionName];
            Assert.NotNull(actualFunctionErrors);
            Assert.Contains("Failed to start language worker process for: node", actualFunctionErrors.First());
        }

        private async Task WaitForWorkerProcessRestart(int restartCount)
        {
            await TestHelpers.Await(() =>
            {
                return GetCurrentWorkerChannel().WorkerId != _nodeWorkerChannel.WorkerId
                         || FunctionErrorsAdded();

            }, pollingInterval: 4 * 1000, timeout: 60 * 1000);
            _nodeWorkerChannel = GetCurrentWorkerChannel();
            _nodeWorkerProcess = GetCurrentWorkerProcess();
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

        private LanguageWorkerChannel GetCurrentWorkerChannel()
        {
            return (LanguageWorkerChannel)_channelState.Channel;
        }

        private ILanguageWorkerProcess GetCurrentWorkerProcess()
        {
            var nodeChannelStates = _channelStates.Where(w => w.Key.Equals(LanguageWorkerConstants.NodeLanguageWorkerName));
            return (ILanguageWorkerProcess)nodeChannelStates.FirstOrDefault().Value.WorkerProcess;
        }

        private bool FunctionErrorsAdded()
        {
            ICollection<string> funcErrors = null;
            return Fixture.JobHost.FunctionErrors.TryGetValue(_functionName, out funcErrors);
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