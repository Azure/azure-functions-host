// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class CSharpEndToEndTests : EndToEndTestsBase<CSharpEndToEndTests.TestFixture>
    {
        private const string JobLogTestFileName = "joblog.txt";

        public CSharpEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task ServiceBusQueueTriggerToBlobTest()
        {
            await ServiceBusQueueTriggerToBlobTestImpl();
        }

        [Fact]
        public async Task EasyTables()
        {
            await EasyTablesTest(isCSharp: true);
        }

        [Fact]
        public async Task DocumentDB()
        {
            await DocumentDBTest();
        }

        [Fact]
        public async Task NotificationHub()
        {
            await NotificationHubTest("NotificationHubOut");
        }

        [Fact]
        public async Task NotificationHub_Out_Notification()
        {
            await Fixture.TouchProjectJson("NotificationHubOutNotification");
            await NotificationHubTest("NotificationHubOutNotification");
        }

        [Fact]
        public async Task EasyTablesTable()
        {
            var id = Guid.NewGuid().ToString();
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { "input",  id }
            };

            await Fixture.TouchProjectJson("EasyTableTable");

            await Fixture.Host.CallAsync("EasyTableTable", arguments);

            await WaitForEasyTableRecordAsync("Item", id);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\CSharp")
            {
                File.Delete(JobLogTestFileName);
                DownloadNuget();
            }

            public async Task TouchProjectJson(string scriptFolderName)
            {
                string scriptPath = Path.Combine(@"TestScripts\CSharp", scriptFolderName);
                string projectLockJson = Path.Combine(scriptPath, "project.lock.json");
                string projectJson = Path.Combine(scriptPath, "project.json");
                if (File.Exists(projectLockJson))
                {
                    // If the file was already there, the host won't restart as nothing
                    // has changed. Assume everything is good and exit.
                    return;
                }

                ScriptHost oldHost = Host;
                File.SetLastWriteTimeUtc(projectJson, DateTime.UtcNow);

                // Wait for the new host to start up.
                await TestHelpers.Await(() =>
                {
                    return !Object.ReferenceEquals(oldHost, Host);
                });
            }

            private void DownloadNuget()
            {
                string fileName = @"Nuget\nuget.exe";
                Directory.CreateDirectory("Nuget");

                fileName = Path.GetFullPath(fileName);

                if (!File.Exists(fileName))
                {
                    WebClient webClient = new WebClient();
                    webClient.DownloadFile("https://dist.nuget.org/win-x86-commandline/latest/nuget.exe", fileName);
                }

                Environment.SetEnvironmentVariable("AzureWebJobs_NuGetPath", fileName);
            }
        }
    }
}
