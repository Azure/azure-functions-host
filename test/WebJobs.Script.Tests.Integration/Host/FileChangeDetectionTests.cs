using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Host
{
    public class FileChangeDetectionTests
    {
        private TestFunctionHost _testHost;
        private static SemaphoreSlim _hostSetup;
        private static SemaphoreSlim _hostStarted;

        public FileChangeDetectionTests()
        {
            _hostSetup = new SemaphoreSlim(1, 2);
            _hostStarted = new SemaphoreSlim(0, 2);
        }

        [Fact]
        public async Task TestFileUpdateWhenHostRestart()
        {
            string testScriptPath = Path.Join("TestScripts", "CSharp");
            string testLogPath = Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs", Guid.NewGuid().ToString(), @"Functions");
            string hostLogPath = Path.Combine(testLogPath, "Host");

            Directory.CreateDirectory(testLogPath);
            Directory.CreateDirectory(hostLogPath);

            string dummyAppOffline = @"<html><head><title>Site Under Construction</title></head><body> Deploying a super important top secret file. shhh!</body></html>";
            File.WriteAllText(Path.Join(testScriptPath, "App_offline.htm"), dummyAppOffline);

            _testHost = new TestFunctionHost(testScriptPath, testLogPath,
                configureWebHostServices: services =>
                {
                    services.AddSingleton<IScriptHostBuilder, PausingScriptHostBuilder>();

                    services.AddSingleton<IConfigureBuilder<IWebJobsBuilder>>(new DelegatedConfigureBuilder<IWebJobsBuilder>(b =>
                    {
                        b.UseHostId("1234");
                        b.Services.Configure<ScriptJobHostOptions>(o => o.Functions = new[] { "ManualTrigger", "Scenarios" });
                    }));
                });
            
            // The Host should start as Offline
            _hostStarted.Wait();
            Assert.Equal("Offline", (await _testHost.GetHostStatusAsync()).State);

            // This should kick off a Host restart and pause once it starts building
            _hostSetup.Wait();
            File.SetLastWriteTimeUtc(Path.Join(testScriptPath, "host.json"), DateTime.UtcNow);

            // We wait for the restart to happen, make sure Host is offline and secretly delete app_offline.htm
            _hostStarted.Wait();
            Assert.Equal("Offline", (await _testHost.GetHostStatusAsync()).State);
            File.Delete(Path.Join(testScriptPath, "App_offline.htm"));

            // Now we let the paused restart to continue
            _hostSetup.Release();

            // Host better be running
            if (!await EnsureHostRunning())
            {
                throw new Exception("Host Status is not 'Running' within the specified time. " +
                    $"Instead found '{(await _testHost.GetHostStatusAsync()).State}'.");
            }

            // Final check
            Assert.Equal("Running", (await _testHost.GetHostStatusAsync()).State);
        }

        private async Task<bool> EnsureHostRunning()
        {
            try
            {
                await TestHelpers.Await(async () =>
                {
                    return (await _testHost.GetHostStatusAsync()).State == "Running";
                }, pollingInterval: 4 * 1000, timeout: 30 * 1000);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private class PausingScriptHostBuilder : IScriptHostBuilder
        {
            private readonly DefaultScriptHostBuilder _inner;
            private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;

            public PausingScriptHostBuilder(IOptionsMonitor<ScriptApplicationHostOptions> options, IServiceProvider root, IServiceScopeFactory scope)
            {
                _inner = new DefaultScriptHostBuilder(options, root, scope);
                _options = options;
            }

            public IHost BuildHost(bool skipHostStartup, bool skipHostConfigurationParsing)
            {
                IHost host = _inner.BuildHost(skipHostStartup, skipHostConfigurationParsing);
                _hostStarted.Release();

                // Grabbing and releasing the semaphore to make sure all setup for tests have been completed
                _hostSetup.Wait();
                _hostSetup.Release();

                return host;
            }
        }
    }
}
