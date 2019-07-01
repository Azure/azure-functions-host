// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Contains methods related to standby mode (placeholder) app initialization.
    /// </summary>
    public class StandbyManager : IStandbyManager, IDisposable
    {
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;
        private readonly Lazy<Task> _specializationTask;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private readonly IEnvironment _environment;
        private readonly IWebHostLanguageWorkerChannelManager _languageWorkerChannelManager;
        private readonly IConfigurationRoot _configuration;
        private readonly ILogger _logger;
        private readonly HostNameProvider _hostNameProvider;
        private readonly IDisposable _changeTokenCallbackSubscription;
        private readonly TimeSpan _specializationTimerInterval;

        private Timer _specializationTimer;
        private static CancellationTokenSource _standbyCancellationTokenSource = new CancellationTokenSource();
        private static IChangeToken _standbyChangeToken = new CancellationChangeToken(_standbyCancellationTokenSource.Token);
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public StandbyManager(IScriptHostManager scriptHostManager, IWebHostLanguageWorkerChannelManager languageWorkerChannelManager, IConfiguration configuration, IScriptWebHostEnvironment webHostEnvironment,
            IEnvironment environment, IOptionsMonitor<ScriptApplicationHostOptions> options, ILogger<StandbyManager> logger, HostNameProvider hostNameProvider)
            : this(scriptHostManager, languageWorkerChannelManager, configuration, webHostEnvironment, environment, options, logger, hostNameProvider, TimeSpan.FromMilliseconds(500))
        {
        }

        public StandbyManager(IScriptHostManager scriptHostManager, IWebHostLanguageWorkerChannelManager languageWorkerChannelManager, IConfiguration configuration, IScriptWebHostEnvironment webHostEnvironment,
            IEnvironment environment, IOptionsMonitor<ScriptApplicationHostOptions> options, ILogger<StandbyManager> logger, HostNameProvider hostNameProvider, TimeSpan specializationTimerInterval)
        {
            _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _specializationTask = new Lazy<Task>(SpecializeHostCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
            _webHostEnvironment = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _configuration = configuration as IConfigurationRoot ?? throw new ArgumentNullException(nameof(configuration));
            _languageWorkerChannelManager = languageWorkerChannelManager ?? throw new ArgumentNullException(nameof(languageWorkerChannelManager));
            _hostNameProvider = hostNameProvider ?? throw new ArgumentNullException(nameof(hostNameProvider));
            _changeTokenCallbackSubscription = ChangeToken.RegisterChangeCallback(_ => _logger.LogDebug($"{nameof(StandbyManager)}.{nameof(ChangeToken)} callback has fired."), null);
            _specializationTimerInterval = specializationTimerInterval;
        }

        public static IChangeToken ChangeToken => _standbyChangeToken;

        public Task SpecializeHostAsync()
        {
            return _specializationTask.Value;
        }

        public async Task SpecializeHostCoreAsync()
        {
            // Go async immediately to ensure that any async context from
            // the PlaceholderSpecializationMiddleware is properly suppressed.
            await Task.Yield();

            _logger.LogInformation(Resources.HostSpecializationTrace);

            // After specialization, we need to ensure that custom timezone
            // settings configured by the user (WEBSITE_TIME_ZONE) are honored.
            // DateTime caches timezone information, so we need to clear the cache.
            TimeZoneInfo.ClearCachedData();

            // Trigger a configuration reload to pick up all current settings
            _configuration?.Reload();

            _hostNameProvider.Reset();

            await _languageWorkerChannelManager.SpecializeAsync();
            NotifyChange();
            await _scriptHostManager.RestartHostAsync();
            await _scriptHostManager.DelayUntilHostReady();
        }

        public void NotifyChange()
        {
            if (_standbyCancellationTokenSource == null)
            {
                return;
            }

            var tokenSource = Interlocked.Exchange(ref _standbyCancellationTokenSource, null);

            if (tokenSource != null &&
                !tokenSource.IsCancellationRequested)
            {
                var changeToken = Interlocked.Exchange(ref _standbyChangeToken, NullChangeToken.Singleton);

                tokenSource.Cancel();

                // Dispose of the token source so our change
                // token reflects that state
                tokenSource.Dispose();
            }
        }

        // for testing
        internal static void ResetChangeToken()
        {
            _standbyCancellationTokenSource = new CancellationTokenSource();
            _standbyChangeToken = new CancellationChangeToken(_standbyCancellationTokenSource.Token);
        }

        public async Task InitializeAsync()
        {
            if (await _semaphore.WaitAsync(timeout: TimeSpan.FromSeconds(30)))
            {
                try
                {
                    await CreateStandbyWarmupFunctions();

                    // start a background timer to identify when specialization happens
                    // specialization usually happens via an http request (e.g. scale controller
                    // ping) but this timer is started as well to handle cases where we
                    // might not receive a request
                    _specializationTimer = new Timer(OnSpecializationTimerTick, null, _specializationTimerInterval, _specializationTimerInterval);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }

        private async Task CreateStandbyWarmupFunctions()
        {
            string scriptPath = _options.CurrentValue.ScriptPath;
            _logger.LogInformation($"Creating StandbyMode placeholder function directory ({scriptPath})");

            await FileUtility.DeleteDirectoryAsync(scriptPath, true);
            FileUtility.EnsureDirectoryExists(scriptPath);

            string content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.Functions.host.json");
            File.WriteAllText(Path.Combine(scriptPath, "host.json"), content);

            content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.Functions.proxies.json");
            File.WriteAllText(Path.Combine(scriptPath, "proxies.json"), content);

            string functionPath = Path.Combine(scriptPath, WarmUpConstants.FunctionName);
            Directory.CreateDirectory(functionPath);
            content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.Functions.{WarmUpConstants.FunctionName}.function.json");
            File.WriteAllText(Path.Combine(functionPath, "function.json"), content);
            content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.Functions.{WarmUpConstants.FunctionName}.run.csx");
            File.WriteAllText(Path.Combine(functionPath, "run.csx"), content);

            _logger.LogInformation($"StandbyMode placeholder function directory created");
        }

        private void OnSpecializationTimerTick(object state)
        {
            if (!_webHostEnvironment.InStandbyMode && _environment.IsContainerReady())
            {
                _specializationTimer?.Dispose();
                _specializationTimer = null;

                SpecializeHostAsync().ContinueWith(t => _logger.LogError(t.Exception, "Error specializing host."),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        public void Dispose()
        {
            _changeTokenCallbackSubscription?.Dispose();
        }
    }
}