using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Host
{
    public class FileChangeDetectionTests
    {
        private static readonly TimeSpan RestartHostTimeout = TimeSpan.FromSeconds(45);
        private static readonly string SemaphoreWaitError = "Unable to grab the semaphore.";
        private static readonly string HostFailedToStart = "Host did not start up in time.";
        private static readonly string HostFailedToRestart = "Host did not restart on file change.";
        private static readonly string DummyAppOffline = @"<html><head><title>Site Under Construction</title></head><body> Deploying a super important top secret file. shhh!</body></html>";

        private TestFunctionHost _testHost;
        private static SemaphoreSlim _hostSetup;
        private static SemaphoreSlim _hostStarted;

        public FileChangeDetectionTests()
        {
            _hostSetup = new SemaphoreSlim(1, 1);
            _hostStarted = new SemaphoreSlim(0, 1);
        }

        [Fact]
        public async Task TestOptionsChange_RestartsHost()
        {
            string sourceFunctionApp = Path.Combine("TestScripts", "CSharp");
            using (var baseTestDir = new TempDirectory())
            {
                string baseTestPath = baseTestDir.Path;
                string oldTestScriptPath = Path.Combine(baseTestPath, "FunctionappOld");
                string newTestScriptPath = Path.Combine(baseTestPath, "FunctionappNew");
                string oldAppOfflinePath = Path.Join(oldTestScriptPath, "App_offline.htm");
                string newAppOfflinePath = Path.Join(newTestScriptPath, "App_offline.htm");
                string testLogPath = Path.Combine(baseTestPath, "Logs", Guid.NewGuid().ToString(), "Functions");
                string hostLogPath = Path.Combine(testLogPath, "Host");

                FileUtility.CopyDirectory(sourceFunctionApp, oldTestScriptPath);
                FileUtility.CopyDirectory(sourceFunctionApp, newTestScriptPath);
                Directory.CreateDirectory(testLogPath);
                Directory.CreateDirectory(hostLogPath);

                File.WriteAllText(oldAppOfflinePath, DummyAppOffline);
                File.WriteAllText(newAppOfflinePath, DummyAppOffline);

                var appOptions = new ScriptApplicationHostOptions
                {
                    IsSelfHost = true,
                    ScriptPath = oldTestScriptPath,
                    LogPath = testLogPath,
                    SecretsPath = Environment.CurrentDirectory,
                    HasParentScope = true
                };

                var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(appOptions);
                var changeTokenSource = new TestChangeTokenSource();
                var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, new[] { changeTokenSource }, factory);

                _testHost = new TestFunctionHost(oldTestScriptPath, testLogPath,
                      configureWebHostServices: services =>
                      {
                          services.Replace(new ServiceDescriptor(typeof(IOptionsMonitor<ScriptApplicationHostOptions>), optionsMonitor));
                          services.AddSingleton<IScriptHostBuilder, PausingScriptHostBuilder>();

                          services.AddSingleton<IConfigureBuilder<IWebJobsBuilder>>(new DelegatedConfigureBuilder<IWebJobsBuilder>(b =>
                          {
                              b.UseHostId("1234");
                              b.Services.Configure<ScriptJobHostOptions>(o => o.Functions = new[] { "ManualTrigger", "Scenarios" });
                          }));
                      });

                // Wait for host to start once
                Assert.True(_hostStarted.Wait(RestartHostTimeout), $"{SemaphoreWaitError} {HostFailedToStart}");
                Assert.True(await EnsureHostState("Offline"), $"Expected Host status Offline. Found : {(await _testHost.GetHostStatusAsync()).State}");

                // Update the script path to point somewhere else and signal the change to OptionsMonitor
                appOptions.ScriptPath = newTestScriptPath;
                changeTokenSource.SignalChange();

                // Wait to ensure that the monitoring service is updated and make a change in the new location
                Assert.True(await EnsureLogMessageReached($"App monitoring service is reloaded.", webHostLevel: true),
                    $"App monitoring service did not reload in time. Logs- {string.Join(Environment.NewLine, _testHost.GetWebHostLogMessages().Select(log => log.ToString()))}");
                File.Delete(newAppOfflinePath);

                // Make sure the change in the new location was picked up and Host's status was changed
                Assert.True(await EnsureHostState("Running"), $"Expected Host status Running. Found : {(await _testHost.GetHostStatusAsync()).State}");

                _testHost.Dispose();
            }
        }

        [Fact]
        public async Task TestFileChangeWhileRestart_RestartsHost()
        {
            string sourceFunctionApp = Path.Combine("TestScripts", "CSharp");
            using (var baseTestDir = new TempDirectory())
            {
                string baseTestPath = baseTestDir.Path;
                string testScriptPath = Path.Combine(baseTestPath, "Functionapp");
                string testLogPath = Path.Combine(baseTestPath, "Logs", Guid.NewGuid().ToString(), "Functions");
                string hostLogPath = Path.Combine(testLogPath, "Host");

                FileUtility.CopyDirectory(sourceFunctionApp, testScriptPath);
                Directory.CreateDirectory(testLogPath);
                Directory.CreateDirectory(hostLogPath);

                string appOfflinePath = Path.Join(testScriptPath, "App_offline.htm");

                File.WriteAllText(appOfflinePath, DummyAppOffline);

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
                Assert.True(_hostStarted.Wait(RestartHostTimeout), $"{SemaphoreWaitError} {HostFailedToStart}");
                Assert.Equal("Offline", (await _testHost.GetHostStatusAsync()).State);

                // This should kick off a Host restart and pause once it starts building
                Assert.True(_hostSetup.Wait(RestartHostTimeout), $"{SemaphoreWaitError} Could not setup for the test.");
                File.SetLastWriteTimeUtc(Path.Join(testScriptPath, "host.json"), DateTime.UtcNow);
                Assert.True(await _hostStarted.WaitAsync(RestartHostTimeout), $"{SemaphoreWaitError} {HostFailedToRestart}");

                // We wait to be sure the old Host is disposed (i.e. Host level FileWatcher is dead)
                if (!await EnsureLogMessageReached("File monitoring service is disposed."))
                {
                    throw new Exception($"Waiting for the old Script Host to dispose timed out. {string.Join(Environment.NewLine, _testHost.GetScriptHostLogMessages().Select(log => log.ToString()))}");
                }

                // At this time, the old host has been properly disposed and the new host start is paused
                // So, the Script Host level File monitoring is not initialized yet.
                // We now delete App_offline.htm and make sure that the event is accurately caught.
                Assert.Equal("Offline", (await _testHost.GetHostStatusAsync()).State);
                File.Delete(Path.Join(testScriptPath, "App_offline.htm"));
                _hostSetup.Release();

                // Host better be running
                if (!await EnsureHostState())
                {
                    throw new Exception("Host Status is not 'Running' within the specified time. " +
                        $"Instead found '{(await _testHost.GetHostStatusAsync()).State}'.");
                }
                Assert.Equal("Running", (await _testHost.GetHostStatusAsync()).State);

                _testHost.Dispose();
            }
        }

        private async Task<bool> EnsureHostState(string state = "Running")
        {
            return await WaitForEventOrTimeout(async () => (await _testHost.GetHostStatusAsync()).State == state);
        }

        private async Task<bool> EnsureLogMessageReached(string logMessage, bool webHostLevel = false)
        {
            return await WaitForEventOrTimeout(async () =>
            {
                var logs = webHostLevel ? _testHost.GetWebHostLogMessages() : _testHost.GetScriptHostLogMessages();
                return await Task.FromResult(logs.Any(log => log.ToString().Contains(logMessage)));
            });
        }

        private async Task<bool> WaitForEventOrTimeout(Func<Task<bool>> conditionFunc)
        {
            try
            {
                await TestHelpers.Await(async () =>
                {
                    return await conditionFunc();
                }, pollingInterval: 4 * 1000, timeout: Convert.ToInt32(RestartHostTimeout.TotalMilliseconds));

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
                IHost testWrappedHost = new WrappedPausingTestHost(host);

                _hostStarted.Release();

                return testWrappedHost;
            }

            private class WrappedPausingTestHost : IHost
            {
                private IHost _innerHost;

                public WrappedPausingTestHost(IHost inner)
                {
                    _innerHost = inner;
                }

                public IServiceProvider Services => _innerHost.Services;

                public void Dispose()
                {
                    _innerHost.Dispose();
                }

                public async Task StartAsync(CancellationToken cancellationToken = default)
                {
                    await Task.Yield();

                    // Grabbing and releasing the semaphore to make sure all setup for tests have been completed
                    Assert.True(_hostSetup.Wait(RestartHostTimeout));
                    _hostSetup.Release();

                    await _innerHost.StartAsync();
                }

                public Task StopAsync(CancellationToken cancellationToken = default)
                {
                    return _innerHost.StopAsync();
                }
            }
        }
    }
}
