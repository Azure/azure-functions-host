// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using Az.ServerlessSecurity.Platform;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ServerlessSecurity
{
    public class ServerlessSecurityDefenderEndToEndTests : IDisposable
    {
        private string _traceloggerFilename;
        private string _localFilePath;
        private string _enableSlsecAgentLog;
        private string _verifyLog;
        private const string LOG_CONFIG = "SERVERLESS_SECURITY_LOG_CONFIG";

        public ServerlessSecurityDefenderEndToEndTests()
        {
            //save original value to reset it to after test
            _enableSlsecAgentLog = Environment.GetEnvironmentVariable(LOG_CONFIG);
            _verifyLog = " message: Start up Serverless Security Agent Handler.";
            _traceloggerFilename = "\\tracelog.txt";
            _localFilePath = Directory.GetCurrentDirectory() + _traceloggerFilename;
            Environment.SetEnvironmentVariable(LOG_CONFIG, _localFilePath);
            File.Delete(_localFilePath);
            File.Create(_localFilePath).Dispose();
        }

        public void Dispose()
        {
            //Delete tracelogger file that was created for the test
            File.Delete(_localFilePath);
            //Reset to initial config value
            Environment.SetEnvironmentVariable(LOG_CONFIG, _enableSlsecAgentLog);
        }

        [Fact]
        public async Task ServerlessSecurityServiceOptions_ServerlessSecurityEnableSetup()
        {
            var env = new TestEnvironment();
            var token = new TestChangeTokenSource<StandbyOptions>();
            // Wire up some options.
            var host = new HostBuilder()
                .ConfigureServices(s =>
                {
                    s.AddSingleton<IEnvironment>(env);
                    s.ConfigureOptions<ServerlessSecurityDefenderOptionsSetup>();
                    s.AddSingleton<IOptionsChangeTokenSource<ServerlessSecurityDefenderOptions>, SpecializationChangeTokenSource<ServerlessSecurityDefenderOptions>>();
                    s.AddSingleton<IOptionsChangeTokenSource<StandbyOptions>>(token);
                    s.AddSingleton<ServerlessSecurityHost>();
                })
                .Build();

            // Placeholder mode
            var options = host.Services.GetService<IOptionsMonitor<ServerlessSecurityDefenderOptions>>();
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureFunctionsSecurityAgentEnabled, null);
            var serverlessSecurityHost = host.Services.GetService<ServerlessSecurityHost>();
            var cancToken = new System.Threading.CancellationToken(false);
            await serverlessSecurityHost.StartAsync(cancToken);
            Assert.Equal(false, options.CurrentValue.EnableDefender);
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureFunctionsSecurityAgentEnabled, "1");
            // still in placeholder mode - should still have the old values.
            Assert.Equal(false, options.CurrentValue.EnableDefender);
            // Simulate specialization, which should refresh.
            token.SignalChange();

            //Assert that config value for enabling defender was changed to true
            await TestHelpers.Await(() => DefenderEnabled());
            Assert.Equal(true, options.CurrentValue.EnableDefender);
        }

        private bool DefenderEnabled()
        {
            bool isSlsecAgentEnabled = false;
            using (StreamReader streamReader = new StreamReader(_localFilePath))
            {
                string[] b = streamReader.ReadToEnd().Split("\n");
                //check the tracelogger file for the message to verify if StartAgent was invoked
                foreach (string line in b)
                {
                    string[] lineArray = line.Split(",");
                    if (lineArray.Length > 1 && lineArray[1].Equals(_verifyLog))
                    {
                        isSlsecAgentEnabled = true;
                        break;
                    }
                }
                //Assert that Agent Handler was recevied a request to StartAgent method
                Assert.Equal(true, isSlsecAgentEnabled);
            }
            return isSlsecAgentEnabled;
        }
    }
}
