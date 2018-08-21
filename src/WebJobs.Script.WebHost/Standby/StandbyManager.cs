// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Contains methods related to standby mode (placeholder) app initialization.
    /// </summary>
    public class StandbyManager : IStandbyManager
    {
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;
        private readonly Lazy<Task> _specializationTask;
        private readonly ILogger _logger;
        private static CancellationTokenSource _standbyCancellationTokenSource = new CancellationTokenSource();
        private static IChangeToken _standbyChangeToken = new CancellationChangeToken(_standbyCancellationTokenSource.Token);
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public StandbyManager(IScriptHostManager scriptHostManager, IOptionsMonitor<ScriptApplicationHostOptions> options, ILoggerFactory loggerFactory)
        {
            _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _specializationTask = new Lazy<Task>(SpecializeHostCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public static IChangeToken ChangeToken => _standbyChangeToken;

        public Task SpecializeHostAsync()
        {
            return _specializationTask.Value;
        }

        public async Task SpecializeHostCoreAsync()
        {
            NotifyChange();

            await _scriptHostManager.RestartHostAsync();
            await _scriptHostManager.DelayUntilHostReady();
        }

        public IChangeToken GetChangeToken() => _standbyChangeToken;

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

        public async Task InitializeAsync()
        {
            if (await _semaphore.WaitAsync(timeout: TimeSpan.FromSeconds(30)))
            {
                try
                {
                    string scriptPath = _options.CurrentValue.ScriptPath;
                    _logger.LogInformation($"Creating StandbyMode placeholder function directory ({scriptPath})");

                    await FileUtility.DeleteDirectoryAsync(scriptPath, true);
                    FileUtility.EnsureDirectoryExists(scriptPath);

                    string content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.Functions.host.json");
                    File.WriteAllText(Path.Combine(scriptPath, "host.json"), content);

                    string functionPath = Path.Combine(scriptPath, WarmUpConstants.FunctionName);
                    Directory.CreateDirectory(functionPath);
                    content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.Functions.{WarmUpConstants.FunctionName}.function.json");
                    File.WriteAllText(Path.Combine(functionPath, "function.json"), content);
                    content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.Functions.{WarmUpConstants.FunctionName}.run.csx");
                    File.WriteAllText(Path.Combine(functionPath, "run.csx"), content);

                    _logger.LogInformation($"StandbyMode placeholder function directory created");
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }
    }
}